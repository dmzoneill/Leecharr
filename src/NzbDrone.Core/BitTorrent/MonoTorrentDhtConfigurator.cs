using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent.Connections;
using MonoTorrent.Connections.Dht;
using MonoTorrent.Dht;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

internal static class MonoTorrentDhtConfigurator
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly object ApplyLock = new();
    private static readonly DhtInboundRateLimiter InboundRateLimiter = new();

    private static FieldInfo bucketMaxCapacityField;
    private static FieldInfo defaultAnnounceIntervalField;
    private static FieldInfo defaultMinimumAnnounceIntervalField;
    private static PropertyInfo bucketRefreshTimeoutProperty;
    private static PropertyInfo messageLoopProperty;
    private static PropertyInfo messageLoopTimeoutProperty;
    private static PropertyInfo messageLoopPendingQueriesProperty;
    private static PropertyInfo routingTableProperty;
    private static MethodInfo preSplitBucketsMethod;
    private static bool reflectionInitialized;

    private static TunedDhtListener tunedListener;

    public static void ApplyStatic(IConfigService config)
    {
        if (config == null)
        {
            return;
        }

        lock (ApplyLock)
        {
            try
            {
                EnsureReflectionInitialized();
                bucketMaxCapacityField.SetValue(null, DhtPolicy.GetBucketSize(config));
                defaultAnnounceIntervalField.SetValue(null, DhtPolicy.GetAnnouncementInterval(config));
                defaultMinimumAnnounceIntervalField.SetValue(null, DhtPolicy.GetMinimumAnnouncementInterval(config));
                InboundRateLimiter.Configure(DhtPolicy.GetMaxInboundQueriesPerSecond(config));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to apply static MonoTorrent DHT settings");
            }
        }
    }

    public static void ApplyEngine(IConfigService config, IDhtEngine dhtEngine)
    {
        if (config == null || dhtEngine == null)
        {
            return;
        }

        lock (ApplyLock)
        {
            try
            {
                EnsureReflectionInitialized();
                ApplyStatic(config);

                if (dhtEngine is not DhtEngine engine)
                {
                    return;
                }

                bucketRefreshTimeoutProperty.SetValue(engine, DhtPolicy.GetBootstrapTimeout(config));

                var messageLoop = messageLoopProperty.GetValue(engine);
                if (messageLoop != null)
                {
                    messageLoopTimeoutProperty.SetValue(messageLoop, DhtPolicy.GetQueryTimeout(config));
                    ApplyListenerTuning(config, engine, messageLoop);
                }

                var routingTable = routingTableProperty.GetValue(engine);
                if (routingTable != null)
                {
                    var targetBuckets = DhtPolicy.GetRoutingTableBucketCount(config);
                    var bucketsProperty = routingTable.GetType().GetProperty("Buckets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var buckets = bucketsProperty?.GetValue(routingTable) as IList;
                    if (buckets != null && buckets.Count < targetBuckets)
                    {
                        preSplitBucketsMethod.Invoke(routingTable, new object[] { targetBuckets });
                    }

                    PruneRoutingTable(routingTable, DhtPolicy.GetMaxRoutingNodes(config));
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to apply MonoTorrent DHT engine settings");
            }
        }
    }

    private static void ApplyListenerTuning(IConfigService config, DhtEngine engine, object messageLoop)
    {
        var listenerObject = messageLoop.GetType().GetProperty("Listener", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(messageLoop);
        if (listenerObject == null)
        {
            return;
        }

        if (listenerObject is TunedDhtListener alreadyTuned)
        {
            tunedListener = alreadyTuned;
            tunedListener.UpdateLimits(DhtPolicy.GetConcurrentQueries(config), () => GetPendingQueries(messageLoop));
            InboundRateLimiter.Configure(DhtPolicy.GetMaxInboundQueriesPerSecond(config));
            return;
        }

        if (listenerObject is not DhtListener listener)
        {
            return;
        }

        if (tunedListener != null && tunedListener.Inner == listener)
        {
            tunedListener.UpdateLimits(DhtPolicy.GetConcurrentQueries(config), () => GetPendingQueries(messageLoop));
            InboundRateLimiter.Configure(DhtPolicy.GetMaxInboundQueriesPerSecond(config));
            return;
        }

        tunedListener = new TunedDhtListener(listener, InboundRateLimiter, DhtPolicy.GetConcurrentQueries(config), () => GetPendingQueries(messageLoop));
        var setListener = typeof(DhtEngine).GetMethod("SetListenerAsync", BindingFlags.Public | BindingFlags.Instance);
        setListener?.Invoke(engine, new object[] { tunedListener });
    }

    private static int GetPendingQueries(object messageLoop)
    {
        return messageLoopPendingQueriesProperty?.GetValue(messageLoop) is int pending ? pending : 0;
    }

    private static void PruneRoutingTable(object routingTable, int maxNodes)
    {
        var bucketsProperty = routingTable.GetType().GetProperty("Buckets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var buckets = bucketsProperty?.GetValue(routingTable) as IList;
        if (buckets == null)
        {
            return;
        }

        var nodes = new List<(object Node, TimeSpan LastSeen)>();
        foreach (var bucket in buckets)
        {
            var bucketNodes = bucket.GetType().GetProperty("Nodes")?.GetValue(bucket) as IEnumerable;
            if (bucketNodes == null)
            {
                continue;
            }

            foreach (var node in bucketNodes)
            {
                var lastSeen = node.GetType().GetProperty("LastSeenDelta")?.GetValue(node) as TimeSpan? ?? TimeSpan.MaxValue;
                nodes.Add((node, lastSeen));
            }
        }

        if (nodes.Count <= maxNodes)
        {
            return;
        }

        foreach (var entry in nodes.OrderByDescending(n => n.LastSeen).Skip(maxNodes))
        {
            foreach (var bucket in buckets)
            {
                var bucketNodes = bucket.GetType().GetProperty("Nodes")?.GetValue(bucket) as IList;
                if (bucketNodes == null)
                {
                    continue;
                }

                for (var i = bucketNodes.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(bucketNodes[i], entry.Node))
                    {
                        bucketNodes.RemoveAt(i);
                    }
                }
            }
        }
    }

    private static void EnsureReflectionInitialized()
    {
        if (reflectionInitialized)
        {
            return;
        }

        var bucketType = Type.GetType("MonoTorrent.Dht.Bucket, MonoTorrent.Dht", throwOnError: true);
        bucketMaxCapacityField = bucketType.GetField("MaxCapacity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Bucket.MaxCapacity field not found");

        var engineType = typeof(DhtEngine);
        defaultAnnounceIntervalField = engineType.GetField("DefaultAnnounceInternal", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("DhtEngine.DefaultAnnounceInternal field not found");
        defaultMinimumAnnounceIntervalField = engineType.GetField("DefaultMinimumAnnounceInterval", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("DhtEngine.DefaultMinimumAnnounceInterval field not found");
        bucketRefreshTimeoutProperty = engineType.GetProperty("BucketRefreshTimeout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("DhtEngine.BucketRefreshTimeout property not found");
        messageLoopProperty = engineType.GetProperty("MessageLoop", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("DhtEngine.MessageLoop property not found");
        routingTableProperty = engineType.GetProperty("RoutingTable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("DhtEngine.RoutingTable property not found");

        var messageLoopType = Type.GetType("MonoTorrent.Dht.MessageLoop, MonoTorrent.Dht", throwOnError: true);
        messageLoopTimeoutProperty = messageLoopType.GetProperty("Timeout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("MessageLoop.Timeout property not found");
        messageLoopPendingQueriesProperty = messageLoopType.GetProperty("PendingQueries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("MessageLoop.PendingQueries property not found");

        var routingTableType = Type.GetType("MonoTorrent.Dht.RoutingTable, MonoTorrent.Dht", throwOnError: true);
        preSplitBucketsMethod = routingTableType.GetMethod("PreSplitBuckets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("RoutingTable.PreSplitBuckets method not found");

        reflectionInitialized = true;
    }

    private sealed class DhtInboundRateLimiter
    {
        private readonly object gate = new();
        private int maxPerSecond = int.MaxValue;
        private int windowCount;
        private long windowStartMs;

        public void Configure(int maxPerSecond)
        {
            lock (this.gate)
            {
                this.maxPerSecond = maxPerSecond <= 0 ? int.MaxValue : maxPerSecond;
                this.windowCount = 0;
                this.windowStartMs = Environment.TickCount64;
            }
        }

        public bool TryAcquire()
        {
            lock (this.gate)
            {
                if (this.maxPerSecond == int.MaxValue)
                {
                    return true;
                }

                var now = Environment.TickCount64;
                if (now - this.windowStartMs >= 1000)
                {
                    this.windowStartMs = now;
                    this.windowCount = 0;
                }

                if (this.windowCount >= this.maxPerSecond)
                {
                    return false;
                }

                this.windowCount++;
                return true;
            }
        }
    }

    private sealed class TunedDhtListener : IDhtListener
    {
        private readonly DhtInboundRateLimiter inboundRateLimiter;
        private readonly DhtListener inner;
        private Func<int> pendingQueries;
        private int maxConcurrentQueries;

        public TunedDhtListener(DhtListener inner, DhtInboundRateLimiter inboundRateLimiter, int maxConcurrentQueries, Func<int> pendingQueries)
        {
            this.inner = inner;
            this.inboundRateLimiter = inboundRateLimiter;
            this.maxConcurrentQueries = Math.Max(1, maxConcurrentQueries);
            this.pendingQueries = pendingQueries;
            this.inner.MessageReceived += this.OnInnerMessageReceived;
        }

        public event Action<ReadOnlyMemory<byte>, IPEndPoint> MessageReceived;

        public event EventHandler<EventArgs> StatusChanged
        {
            add => this.inner.StatusChanged += value;
            remove => this.inner.StatusChanged -= value;
        }

        public DhtListener Inner => this.inner;

        public void UpdateLimits(int maxConcurrentQueries, Func<int> pendingQueries)
        {
            this.maxConcurrentQueries = Math.Max(1, maxConcurrentQueries);
            this.pendingQueries = pendingQueries;
        }

        public IPEndPoint LocalEndPoint => this.inner.LocalEndPoint;

        public IPEndPoint PreferredLocalEndPoint
        {
            get => this.inner.PreferredLocalEndPoint;
            set => this.inner.PreferredLocalEndPoint = value;
        }

        public ListenerStatus Status => this.inner.Status;

        public void Start() => this.inner.Start();

        public void Stop() => this.inner.Stop();

        public Task SendAsync(ReadOnlyMemory<byte> buffer, IPEndPoint endPoint)
        {
            while (this.pendingQueries() >= this.maxConcurrentQueries)
            {
                Thread.Sleep(1);
            }

            return this.inner.SendAsync(buffer, endPoint);
        }

        private void OnInnerMessageReceived(ReadOnlyMemory<byte> buffer, IPEndPoint endPoint)
        {
            if (!this.inboundRateLimiter.TryAcquire())
            {
                return;
            }

            this.MessageReceived?.Invoke(buffer, endPoint);
        }
    }
}
