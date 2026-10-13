// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net.Mail;
using System.Net.Security;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class EmailNotificationSenderTest
{
    [Test]
    public async Task SendEmailNotificationAsync_WhenSubjectContainsCrlf_SanitizesSubjectWithoutThrowing()
    {
        var settings = "{\"server\":\"localhost\",\"port\":25,\"from\":\"sender@example.com\",\"to\":\"recipient@example.com\"}";
        var torrent = new Torrent
        {
            Name = "Ubuntu.24.04\r\nSubject-Injection: MaliciousHeader\r\n\r\nInjected Body",
            Category = "linux",
            Progress = 1.0,
            Status = TorrentStatus.Seeding,
            TotalSize = 1024 * 1024 * 500,
        };

        MailMessage interceptedMail = null!;

        await EmailNotificationSender.SendEmailNotificationAsync(
            settings,
            "OnDownloadComplete",
            torrent,
            null,
            null,
            (client, mail) =>
            {
                interceptedMail = mail;
                return Task.CompletedTask;
            });

        interceptedMail.Should().NotBeNull();
        interceptedMail.Subject.Should().NotContain("\r");
        interceptedMail.Subject.Should().NotContain("\n");
        interceptedMail.Subject.Should().Be("[Leecharr] [OnDownloadComplete] Ubuntu.24.04 Subject-Injection: MaliciousHeader Injected Body");
    }

    [Test]
    public void SendEmailNotification_WhenGenericPayloadContainsCrlf_SanitizesSubject()
    {
        var settings = "server=localhost&port=25&from=sender@example.com&to=recipient@example.com";
        var genericPayload = new { Message = "Error occurred:\r\nDisk full\nCheck /data" };

        MailMessage interceptedMail = null!;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "OnError",
            null,
            null,
            genericPayload,
            (client, mail) =>
            {
                interceptedMail = mail;
            });

        interceptedMail.Should().NotBeNull();
        interceptedMail.Subject.Should().NotContain("\r");
        interceptedMail.Subject.Should().NotContain("\n");
        interceptedMail.Subject.Should().Be("[Leecharr] [OnError] Error occurred: Disk full Check /data");
    }

    [Test]
    public async Task SendEmailNotificationAsync_WhenRecipientsSeparatedBySemicolons_ParsesAllRecipients()
    {
        var settings = "{\"server\":\"localhost\",\"from\":\"bot@example.com\",\"to\":\"alerts@example.com; admin@example.com; ops@example.com\"}";

        MailMessage interceptedMail = null!;

        await EmailNotificationSender.SendEmailNotificationAsync(
            settings,
            "OnGrab",
            null,
            null,
            new { Message = "Grabbed release" },
            (client, mail) =>
            {
                interceptedMail = mail;
                return Task.CompletedTask;
            });

        interceptedMail.Should().NotBeNull();
        interceptedMail.To.Should().HaveCount(3);
        interceptedMail.To[0].Address.Should().Be("alerts@example.com");
        interceptedMail.To[1].Address.Should().Be("admin@example.com");
        interceptedMail.To[2].Address.Should().Be("ops@example.com");
    }

    [Test]
    public void SendEmailNotification_WhenRecipientsContainMixedCommasAndSemicolons_ParsesAllRecipients()
    {
        var settings = "server=localhost&from=bot@example.com&to=first@test.com; second@test.com, third@test.com ; fourth@test.com";

        MailMessage interceptedMail = null!;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Test",
            null,
            null,
            new { Message = "Test" },
            (client, mail) =>
            {
                interceptedMail = mail;
            });

        interceptedMail.Should().NotBeNull();
        interceptedMail.To.Should().HaveCount(4);
        interceptedMail.To[0].Address.Should().Be("first@test.com");
        interceptedMail.To[1].Address.Should().Be("second@test.com");
        interceptedMail.To[2].Address.Should().Be("third@test.com");
        interceptedMail.To[3].Address.Should().Be("fourth@test.com");
    }

    [TestCase("{\"server\":\"relay.internal\",\"port\":587,\"ssl\":true,\"ignoreSslErrors\":true,\"to\":\"dest@example.com\"}")]
    [TestCase("server=relay.internal&port=587&ssl=true&validateCertificate=false&to=dest@example.com")]
    [TestCase("{\"server\":\"relay.internal\",\"port\":587,\"ssl\":true,\"ignoreCertificateErrors\":true,\"to\":\"dest@example.com\"}")]
    [TestCase("server=relay.internal&port=587&ssl=true&allowInvalidCertificates=true&to=dest@example.com")]
    public async Task SendEmailNotificationAsync_WhenCertificateChecksAreDisabled_LeavesProcessCertificateValidationUnchanged(string settings)
    {
#pragma warning disable SYSLIB0014
        var originalCallback = System.Net.ServicePointManager.ServerCertificateValidationCallback;
        RemoteCertificateValidationCallback sentinel = (_, _, _, _) => false;
        System.Net.ServicePointManager.ServerCertificateValidationCallback = sentinel;
        try
        {
            RemoteCertificateValidationCallback callbackDuringSend = null;

            await EmailNotificationSender.SendEmailNotificationAsync(
                settings,
                "Test",
                null,
                null,
                new { Message = "Test SSL" },
                (client, mail) =>
                {
                    callbackDuringSend = System.Net.ServicePointManager.ServerCertificateValidationCallback;
                    return Task.CompletedTask;
                });

            callbackDuringSend.Should().BeSameAs(sentinel);
            System.Net.ServicePointManager.ServerCertificateValidationCallback.Should().BeSameAs(sentinel);
        }
        finally
        {
            System.Net.ServicePointManager.ServerCertificateValidationCallback = originalCallback;
        }
#pragma warning restore SYSLIB0014
    }

    [Test]
    public async Task SendEmailNotificationAsync_WhenIgnoreSslErrorsOverlaps_DoesNotReplaceProcessCertificateValidation()
    {
        var settings = "{\"server\":\"relay.internal\",\"port\":587,\"ssl\":true,\"ignoreSslErrors\":true,\"to\":\"dest@example.com\"}";
#pragma warning disable SYSLIB0014
        var originalCallback = System.Net.ServicePointManager.ServerCertificateValidationCallback;
        RemoteCertificateValidationCallback sentinel = (_, _, _, _) => false;
        System.Net.ServicePointManager.ServerCertificateValidationCallback = sentinel;
        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var first = EmailNotificationSender.SendEmailNotificationAsync(
                settings,
                "Test",
                null,
                null,
                new { Message = "Test SSL overlap" },
                async (client, mail) =>
                {
                    started.TrySetResult();
                    await release.Task.ConfigureAwait(false);
                    System.Net.ServicePointManager.ServerCertificateValidationCallback.Should().BeSameAs(sentinel);
                });

            try
            {
                await started.Task.ConfigureAwait(false);

                await EmailNotificationSender.SendEmailNotificationAsync(
                    settings,
                    "Test",
                    null,
                    null,
                    new { Message = "Test SSL overlap" },
                    (client, mail) =>
                    {
                        System.Net.ServicePointManager.ServerCertificateValidationCallback.Should().BeSameAs(sentinel);
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult();
            }

            await first.ConfigureAwait(false);
            System.Net.ServicePointManager.ServerCertificateValidationCallback.Should().BeSameAs(sentinel);
        }
        finally
        {
            System.Net.ServicePointManager.ServerCertificateValidationCallback = originalCallback;
        }
#pragma warning restore SYSLIB0014
    }

    [Test]
    public void CreateSmtpCertificateValidationCallback_WhenIgnoreSslErrorsIsFalse_ReturnsNull()
    {
        EmailNotificationSender.CreateSmtpCertificateValidationCallback(false).Should().BeNull();
    }

    [TestCase(SslPolicyErrors.None)]
    [TestCase(SslPolicyErrors.RemoteCertificateChainErrors)]
    [TestCase(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [TestCase(SslPolicyErrors.RemoteCertificateNotAvailable)]
    public void CreateSmtpCertificateValidationCallback_WhenIgnoreSslErrorsIsTrue_AcceptsCertificateErrors(SslPolicyErrors errors)
    {
        var callback = EmailNotificationSender.CreateSmtpCertificateValidationCallback(true);

        callback.Should().NotBeNull();
        callback(null, null, null, errors).Should().BeTrue();
    }

    [Test]
    public void SendEmailNotification_WhenToContainsOnlyDelimiters_ThrowsInvalidOperationException()
    {
        var settings = "{\"server\":\"localhost\",\"to\":\"  ; , ; \"}";
        var act = () => EmailNotificationSender.SendEmailNotification(settings, "Test", null, null, new { Message = "Test" });
        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void SendEmailNotification_WhenSettingsAreEmpty_ThrowsArgumentException()
    {
        var act = () => EmailNotificationSender.SendEmailNotification(string.Empty, "Test", null, null, new { Message = "Test" });
        act.Should().Throw<ArgumentException>();
    }

    [TestCase(465, true, true)]
    [TestCase(465, false, false)]
    [TestCase(587, true, false)]
    [TestCase(25, true, false)]
    public void RequiresImplicitSsl_IdentifiesSmtpsPort(int port, bool useSsl, bool expected)
    {
        EmailNotificationSender.RequiresImplicitSsl(useSsl, port).Should().Be(expected);
    }
}
