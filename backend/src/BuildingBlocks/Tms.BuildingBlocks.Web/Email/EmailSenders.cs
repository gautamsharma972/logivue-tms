using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Tms.SharedKernel.Contracts;

namespace Tms.BuildingBlocks.Web.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary><c>Log</c> writes mails to the log (development, tests); <c>Smtp</c> sends them.</summary>
    public string Provider { get; init; } = "Log";

    public string? Host { get; init; }

    public int Port { get; init; } = 587;

    public bool UseStartTls { get; init; } = true;

    public string? User { get; init; }

    public string? Password { get; init; }

    public string FromAddress { get; init; } = "no-reply@tms.local";

    public string FromName { get; init; } = "TMS";

    /// <summary>Public URL of the web app; used to build links in emails (e.g. password reset).</summary>
    public string AppBaseUrl { get; init; } = "http://localhost:5173";
}

/// <summary>Development sender: the "inbox" is the log, so reset links can be copied from the console.</summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("EMAIL (not sent; Email:Provider=Log) to {To}: {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host))
        {
            throw new InvalidOperationException("Email:Host must be set when Email:Provider is Smtp.");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(o.Host, o.Port, o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, cancellationToken);
            if (!string.IsNullOrEmpty(o.User))
            {
                await client.AuthenticateAsync(o.User, o.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mail trouble must not break the user's flow (e.g. "forgot password" must answer the same either way).
            logger.LogError(ex, "Failed to send email '{Subject}' to {To}", message.Subject, message.To);
        }
    }
}
