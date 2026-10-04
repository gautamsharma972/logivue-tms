namespace Tms.SharedKernel.Contracts;

public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>Outbound email. Implementations must not throw for an unreachable mail server in a way that breaks the caller's flow.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
