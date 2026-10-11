// Copyright (c) FeedItOut. All rights reserved.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Notifications;

public interface INotificationEndpointTester
{
    Task<int> TestEndpointsAsync(int? notificationId, bool sendTestPayload, CancellationToken cancellationToken = default);
}

public class NotificationEndpointTester : INotificationEndpointTester
{
    private readonly INotificationRepository notificationRepository;
    private readonly IWebhookDispatcher webhookDispatcher;
    private readonly ICustomScriptService? customScriptService;
    private readonly Logger logger;

    public NotificationEndpointTester(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService? customScriptService = null)
    {
        this.notificationRepository = notificationRepository;
        this.webhookDispatcher = webhookDispatcher;
        this.customScriptService = customScriptService;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<int> TestEndpointsAsync(int? notificationId, bool sendTestPayload, CancellationToken cancellationToken = default)
    {
        if (this.notificationRepository == null || this.webhookDispatcher == null)
        {
            return 0;
        }

        if (!sendTestPayload)
        {
            this.logger.Info("Skipping notification test dispatch because SendTestPayload=false");
            return 0;
        }

        var targets = notificationId.HasValue
            ? this.notificationRepository.Get(notificationId.Value) is { } one
                ? new[] { one }
                : Array.Empty<NotificationDefinition>()
            : this.notificationRepository.GetEnabled().ToArray();

        var successCount = 0;
        foreach (var notif in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await this.TestSingleAsync(notif, cancellationToken).ConfigureAwait(false))
            {
                successCount++;
            }
        }

        return successCount;
    }

    private async Task<bool> TestSingleAsync(NotificationDefinition notif, CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                if (this.customScriptService == null)
                {
                    return false;
                }

                var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
                return await this.customScriptService.ExecuteScriptAsync(scriptPath, null, "Test", scriptArgs)
                    .ConfigureAwait(false);
            }

            if (string.Equals(notif.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
            {
                var payload = new { EventType = "Test", Message = "Leecharr test notification", Timestamp = DateTime.UtcNow };
                await NotificationEventHandler.SendEmailNotificationAsync(notif.Settings, "Test", null, null, payload)
                    .ConfigureAwait(false);
                return true;
            }

            var webhookPayload = new
            {
                EventType = "Test",
                Message = "Leecharr test notification",
                Timestamp = DateTime.UtcNow,
            };

            var targetUrl = NotificationEventHandler.ResolveTargetUrl(notif.Implementation, notif.Settings);
            var customHeaders = NotificationEventHandler.ResolveCustomHeaders(notif.Implementation, notif.Settings);
            var httpMethod = NotificationEventHandler.ResolveHttpMethod(notif.Implementation, notif.Settings);
            return await this.webhookDispatcher.DispatchAsync(targetUrl, webhookPayload, customHeaders, httpMethod, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Notification test failed for {0} ({1})", notif.Name, notif.Implementation);
            return false;
        }
    }
}
