using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Alerts;

public interface IAlertService
{
    Task<PagedResult<AlertDto>> SearchAsync(AlertSearch search, CancellationToken cancellationToken = default);

    Task<AlertDto> AcknowledgeAsync(long alertId, CancellationToken cancellationToken = default);

    Task<AlertDto> ResolveAsync(long alertId, string? comments, CancellationToken cancellationToken = default);
}

public sealed class AlertService(
    IRepository<TransporterAlert> alerts,
    ITransporterQueries queries,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    TimeProvider clock) : IAlertService
{
    public Task<PagedResult<AlertDto>> SearchAsync(AlertSearch search, CancellationToken cancellationToken = default) =>
        queries.SearchAlertsAsync(search with
        {
            Page = Paging.NormalisePage(search.Page),
            PageSize = Paging.NormalisePageSize(search.PageSize)
        }, cancellationToken);

    public async Task<AlertDto> AcknowledgeAsync(long alertId, CancellationToken cancellationToken = default)
    {
        var alert = await LoadAsync(alertId, cancellationToken);
        if (alert.Status != AlertStatus.Open)
        {
            throw new BusinessRuleException($"Alert {alertId} is {alert.Status} and cannot be acknowledged.", "ILLEGAL_TRANSITION");
        }

        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedAt = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterAlert", alertId.ToString(), "AlertAcknowledged"), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(alert);
    }

    public async Task<AlertDto> ResolveAsync(long alertId, string? comments, CancellationToken cancellationToken = default)
    {
        var alert = await LoadAsync(alertId, cancellationToken);
        if (alert.Status == AlertStatus.Resolved)
        {
            throw new BusinessRuleException($"Alert {alertId} is already resolved.", "ILLEGAL_TRANSITION");
        }

        alert.Status = AlertStatus.Resolved;
        alert.ResolvedAt = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterAlert", alertId.ToString(), "AlertResolved", Reason: comments?.Trim()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(alert);
    }

    private async Task<TransporterAlert> LoadAsync(long alertId, CancellationToken cancellationToken) =>
        await alerts.FindAsync(alertId, cancellationToken)
        ?? throw new NotFoundException($"Alert {alertId} was not found.", "ALERT_NOT_FOUND");

    private static AlertDto ToDto(TransporterAlert a) =>
        new(a.Id, a.AlertType, a.Severity, a.TransporterId, a.LoadReference, a.EntityType, a.EntityId, a.Message,
            a.CreatedAt, a.DueAt, a.Status, a.AcknowledgedAt, a.ResolvedAt);
}
