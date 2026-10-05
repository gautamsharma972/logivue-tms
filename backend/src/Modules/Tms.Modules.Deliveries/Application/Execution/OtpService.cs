using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Tms.Modules.Deliveries.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Deliveries.Application.Execution;

/// <summary>Issues the one-time code a customer reads out to prove they received the goods. The code is emailed and never stored or logged: only a salted hash is kept.</summary>
internal sealed class OtpService(IEmailSender email, IDeliverySettings settings, ILogger<OtpService> logger)
{
    public async Task<OtpIssuedDto> IssueAsync(Delivery delivery, Actor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rules = await settings.GetAsync<PodRulesSetting>(DeliverySettingKeys.PodRules, cancellationToken);
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        delivery.IssueOtp(code, rules.OtpValidityMinutes, actor, now);

        var sent = false;
        string? channel = null;
        if (!string.IsNullOrWhiteSpace(delivery.CustomerEmail))
        {
            try
            {
                await email.SendAsync(new EmailMessage(delivery.CustomerEmail, $"Your delivery code for {delivery.Number}",
                    $"Your delivery from {delivery.TransporterReference ?? "the transporter"} has arrived. Give this code to the driver to confirm you received it: {code}\n\nIt expires in {rules.OtpValidityMinutes} minutes."), cancellationToken);
                sent = true;
                channel = "Email";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not send the delivery code for {Delivery}", delivery.Number);
            }
        }

        return new OtpIssuedDto(sent, channel, now.AddMinutes(rules.OtpValidityMinutes));
    }
}
