// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public static class EmailNotificationSender
{
    public static void SendEmailNotification(
        string settings,
        string eventType,
        Torrent torrent,
        dynamic meta,
        object genericPayload,
        Action<SmtpClient, MailMessage> smtpSender = null)
    {
        Func<SmtpClient, MailMessage, Task> asyncSender = null;
        if (smtpSender != null)
        {
            asyncSender = (client, mail) =>
            {
                smtpSender(client, mail);
                return Task.CompletedTask;
            };
        }

        SendEmailNotificationAsync(settings, eventType, torrent, meta, genericPayload, asyncSender)
            .GetAwaiter().GetResult();
    }

    public static Task SendEmailNotificationAsync(
        string settings,
        string eventType,
        Torrent torrent,
        dynamic meta,
        object genericPayload,
        Action<SmtpClient, MailMessage> smtpSender)
    {
        Func<SmtpClient, MailMessage, Task> asyncSender = null;
        if (smtpSender != null)
        {
            asyncSender = (client, mail) =>
            {
                smtpSender(client, mail);
                return Task.CompletedTask;
            };
        }

        return SendEmailNotificationAsync(settings, eventType, torrent, meta, genericPayload, asyncSender);
    }

    public static async Task SendEmailNotificationAsync(
        string settings,
        string eventType,
        Torrent torrent,
        dynamic meta,
        object genericPayload,
        Func<SmtpClient, MailMessage, Task> smtpSenderAsync = null)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            throw new ArgumentException("Email settings are required.", nameof(settings));
        }

        var host = "localhost";
        var port = 25;
        var ssl = false;
        var ignoreSslErrors = false;
        string user = null;
        string pass = null;
        var from = "leecharr@localhost";
        string to = null;

        if (settings.TrimStart().StartsWith("{"))
        {
            using var doc = JsonDocument.Parse(settings);
            var root = doc.RootElement;
            if (root.TryGetProperty("server", out var s) || root.TryGetProperty("host", out s))
            {
                host = s.GetString() ?? host;
            }

            if (root.TryGetProperty("port", out var p))
            {
                if (p.TryGetInt32(out var pInt))
                {
                    port = pInt;
                }
                else if (int.TryParse(p.GetString(), out var pParsed))
                {
                    port = pParsed;
                }
            }

            if (root.TryGetProperty("useSsl", out var sslProp) || root.TryGetProperty("ssl", out sslProp))
            {
                ssl = sslProp.GetBoolean();
            }

            if (root.TryGetProperty("ignoreSslErrors", out var ignoreSslProp) ||
                root.TryGetProperty("ignoreCertificateErrors", out ignoreSslProp) ||
                root.TryGetProperty("allowInvalidCertificates", out ignoreSslProp))
            {
                ignoreSslErrors = ignoreSslProp.GetBoolean();
            }
            else if (root.TryGetProperty("validateCertificate", out var validateCertProp) ||
                     root.TryGetProperty("validateCertificates", out validateCertProp))
            {
                ignoreSslErrors = !validateCertProp.GetBoolean();
            }

            if (root.TryGetProperty("username", out var u) || root.TryGetProperty("user", out u))
            {
                user = u.GetString();
            }

            if (root.TryGetProperty("password", out var pwd) || root.TryGetProperty("pass", out pwd))
            {
                pass = pwd.GetString();
            }

            if (root.TryGetProperty("from", out var f))
            {
                from = f.GetString() ?? from;
            }

            if (root.TryGetProperty("to", out var tProp) || root.TryGetProperty("recipient", out tProp))
            {
                to = tProp.GetString();
            }
        }
        else if (settings.Contains('='))
        {
            var pairs = settings.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(parts[0]).Trim().ToLowerInvariant();
                var val = Uri.UnescapeDataString(parts[1]).Trim();
                switch (key)
                {
                    case "server":
                    case "host":
                        host = val;
                        break;
                    case "port":
                        if (int.TryParse(val, out var p))
                        {
                            port = p;
                        }

                        break;
                    case "ssl":
                    case "usessl":
                        if (bool.TryParse(val, out var s))
                        {
                            ssl = s;
                        }

                        break;
                    case "ignoresslerrors":
                    case "ignorecertificateerrors":
                    case "allowinvalidcertificates":
                        if (bool.TryParse(val, out var ign))
                        {
                            ignoreSslErrors = ign;
                        }

                        break;
                    case "validatecertificate":
                    case "validatecertificates":
                        if (bool.TryParse(val, out var valCert))
                        {
                            ignoreSslErrors = !valCert;
                        }

                        break;
                    case "user":
                    case "username":
                        user = val;
                        break;
                    case "pass":
                    case "password":
                        pass = val;
                        break;
                    case "from":
                    case "sender":
                        from = val;
                        break;
                    case "to":
                    case "recipient":
                        to = val;
                        break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            throw new InvalidOperationException("Recipient email address ('to') is required.");
        }

        var torrentName = torrent?.Name ?? NotificationPayloadBuilder.ExtractMessage(genericPayload, eventType);
        var rawSubject = $"[Leecharr] [{eventType}] {torrentName}";
        var subject = Regex.Replace(rawSubject, @"[\r\n]+", " ").Trim();

        var torrentDetails = torrent != null
            ? $"Torrent: {torrent.Name}\nCategory: {torrent.Category ?? "None"}\nProgress: {torrent.Progress * 100:F1}%\nStatus: {torrent.Status}\nSize: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
            : NotificationPayloadBuilder.ExtractMessage(genericPayload, $"Event: {eventType}");
        var overview = NotificationPayloadBuilder.ExtractOverview(meta);
        var body = !string.IsNullOrWhiteSpace(overview)
            ? $"{torrentDetails}\n\n{overview}"
            : torrentDetails;

        using var mail = new MailMessage();
        mail.From = new MailAddress(from.Trim());
        mail.Subject = subject;
        mail.Body = body;

        var recipients = to.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var recipient in recipients)
        {
            var trimmedRecipient = recipient.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedRecipient))
            {
                mail.To.Add(new MailAddress(trimmedRecipient));
            }
        }

        if (mail.To.Count == 0)
        {
            throw new InvalidOperationException("Recipient email address ('to') is required.");
        }

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = ssl,
            Timeout = 10000,
        };

        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
        {
            client.Credentials = new NetworkCredential(user, pass);
        }

#pragma warning disable SYSLIB0014
        RemoteCertificateValidationCallback previousCallback = null;
        if (ignoreSslErrors)
        {
            previousCallback = ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
        }

        try
        {
            if (smtpSenderAsync != null)
            {
                await smtpSenderAsync(client, mail).ConfigureAwait(false);
            }
            else
            {
                await client.SendMailAsync(mail).ConfigureAwait(false);
            }
        }
        finally
        {
            if (ignoreSslErrors)
            {
                ServicePointManager.ServerCertificateValidationCallback = previousCallback;
            }
        }
#pragma warning restore SYSLIB0014
    }
}
