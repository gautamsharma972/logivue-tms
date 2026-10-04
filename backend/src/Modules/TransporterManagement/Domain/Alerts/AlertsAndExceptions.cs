using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Domain.Alerts;

public class TransporterAlert
{
    public long Id { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public long TransporterId { get; set; }
    public string? LoadReference { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Open;
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class TransporterException
{
    public long Id { get; set; }
    public string ExceptionType { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public long TransporterId { get; set; }
    public string? LoadReference { get; set; }
    public string? RootCause { get; set; }
    public string? Owner { get; set; }
    public ExceptionStatus Status { get; set; } = ExceptionStatus.Open;
    public DateTime CreatedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ActionTaken { get; set; }
}
