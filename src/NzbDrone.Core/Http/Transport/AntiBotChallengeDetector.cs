// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace NzbDrone.Core.Http.Transport;

public static class AntiBotChallengeDetector
{
    /// <summary>
    /// Maximum bytes read from a response body when probing for anti-bot challenge signatures.
    /// Challenge pages are small HTML documents; large indexer payloads must not be buffered here.
    /// </summary>
    public const int MaxChallengeProbeBytes = 256 * 1024;

    private static readonly string[] ChallengeSignatures = new[]
    {
        "Just a moment...",
        "Attention Required! | Cloudflare",
        "Security Check | DDoS-GUARD",
        "cf-browser-verification",
        "cf-turnstile",
        "turnstile",
        "challenges.cloudflare.com",
        "_cf_chl_opt",
        "_cf_chl_ctx",
        "cloudflare-nginx",
        "ddos-guard",
        "Checking your browser before accessing",
        "Checking your browser",
        "ray id",
        "cf_clearance",
    };

    public static bool IsChallenge(HttpStatusCode statusCode, string bodyHtml, HttpResponseMessage response = null)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
        {
            if (response != null && (statusCode == HttpStatusCode.Forbidden || statusCode == HttpStatusCode.ServiceUnavailable || (int)statusCode == 429))
            {
                if (response.Headers.Contains("cf-mitigated") ||
                    response.Headers.Contains("cf-ray") ||
                    (response.Headers.Server != null && response.Headers.Server.ToString().Contains("cloudflare", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var signature in ChallengeSignatures)
        {
            if (bodyHtml.Contains(signature, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (response != null && response.Headers.Contains("cf-mitigated"))
        {
            var values = response.Headers.GetValues("cf-mitigated");
            if (values.Any(v => v.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsChallenge(string bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
        {
            return false;
        }

        foreach (var signature in ChallengeSignatures)
        {
            if (bodyHtml.Contains(signature, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<bool> IsChallengeAsync(HttpResponseMessage response)
    {
        if (response == null)
        {
            return false;
        }

        var isPotentialChallengeCode = response.StatusCode == HttpStatusCode.Forbidden
                                    || response.StatusCode == HttpStatusCode.ServiceUnavailable
                                    || (int)response.StatusCode == 429
                                    || response.StatusCode == HttpStatusCode.OK;

        var hasCfHeaders = response.Headers.Contains("cf-mitigated")
                        || response.Headers.Contains("cf-ray")
                        || (response.Headers.Server != null && response.Headers.Server.ToString().Contains("cloudflare", StringComparison.OrdinalIgnoreCase));

        if (!isPotentialChallengeCode && !hasCfHeaders)
        {
            return false;
        }

        if (response.Headers.Contains("cf-mitigated"))
        {
            var values = response.Headers.GetValues("cf-mitigated");
            if (values.Any(v => v.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (response.Content == null)
        {
            return (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.ServiceUnavailable) && hasCfHeaders;
        }

        try
        {
            var body = await ReadChallengeProbeBodyAsync(response);
            return IsChallenge(response.StatusCode, body, response);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ReadChallengeProbeBodyAsync(HttpResponseMessage response)
    {
        var content = response.Content;
        if (content == null)
        {
            return string.Empty;
        }

        var sourceStream = await content.ReadAsStreamAsync();
        var prefix = new byte[MaxChallengeProbeBytes];
        var readBuffer = new byte[81920];
        var totalRead = 0;

        while (totalRead < MaxChallengeProbeBytes)
        {
            var toRead = Math.Min(readBuffer.Length, MaxChallengeProbeBytes - totalRead);
            var read = await sourceStream.ReadAsync(readBuffer.AsMemory(0, toRead));
            if (read == 0)
            {
                break;
            }

            Buffer.BlockCopy(readBuffer, 0, prefix, totalRead, read);
            totalRead += read;
        }

        var hasRemainder = false;
        byte pushbackByte = 0;
        if (totalRead == MaxChallengeProbeBytes)
        {
            var extra = await sourceStream.ReadAsync(readBuffer.AsMemory(0, 1));
            if (extra > 0)
            {
                hasRemainder = true;
                pushbackByte = readBuffer[0];
            }
        }

        Stream bodyStream;
        if (!hasRemainder)
        {
            bodyStream = new MemoryStream(prefix, 0, totalRead, writable: false);
            if (sourceStream != null)
            {
                await sourceStream.DisposeAsync();
            }
        }
        else
        {
            var prefixStream = new MemoryStream(prefix, 0, totalRead, writable: false);
            bodyStream = new ConcatenatingStream(prefixStream, new PushbackStream(sourceStream, pushbackByte));
        }

        var replacement = new StreamContent(bodyStream);
        foreach (var header in content.Headers)
        {
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        content.Dispose();
        response.Content = replacement;

        return totalRead == 0 ? string.Empty : Encoding.UTF8.GetString(prefix, 0, totalRead);
    }

    private sealed class PushbackStream : Stream
    {
        private readonly Stream inner;
        private readonly byte pushbackByte;
        private bool pushbackConsumed;

        public PushbackStream(Stream inner, byte pushbackByte)
        {
            this.inner = inner;
            this.pushbackByte = pushbackByte;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => this.inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!this.pushbackConsumed)
            {
                this.pushbackConsumed = true;
                buffer[offset] = this.pushbackByte;
                return 1;
            }

            return this.inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ConcatenatingStream : Stream
    {
        private readonly Stream first;
        private readonly Stream second;
        private bool onSecond;

        public ConcatenatingStream(Stream first, Stream second)
        {
            this.first = first;
            this.second = second;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!this.onSecond)
            {
                var read = this.first.Read(buffer, offset, count);
                if (read > 0)
                {
                    return read;
                }

                this.onSecond = true;
            }

            return this.second.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.first.Dispose();
                this.second.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
