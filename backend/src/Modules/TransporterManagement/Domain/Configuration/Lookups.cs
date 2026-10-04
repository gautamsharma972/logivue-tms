namespace LogiVue.Tms.TransporterManagement.Domain.Configuration;

/// <summary>Configurable transporter type (FTL, PTL, 3PL, ...). Values are data, not code.</summary>
public class TransporterType
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>Configurable capability (Hazardous, Temperature Controlled, ...).</summary>
public class CapabilityType
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>Configurable compliance document type and its compliance rules.</summary>
public class DocumentType
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public bool IsTransporterLevel { get; set; } = true;
    public bool IsVehicleLevel { get; set; }
    public bool IsDriverLevel { get; set; }
    public bool VerificationRequired { get; set; } = true;
    public bool ExpiryRequired { get; set; }
    public int? RenewalReminderDays { get; set; }
    public bool BlockAllocationWhenExpired { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Key/value business configuration (KPI weights, SLAs, thresholds). Values are JSON so each setting
/// can hold a structured object. <see cref="Version"/> increments on every change.
/// </summary>
public class ConfigurationSetting
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "{}";
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// One review stage in the onboarding workflow. Stages are data, so approval levels can be added,
/// reordered or re-assigned to roles without a code change. <see cref="Status"/> is the transporter
/// status while the transporter is in this stage.
/// </summary>
public class OnboardingStep
{
    public long Id { get; set; }
    public int Sequence { get; set; }
    public Common.TransporterStatus Status { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RequiredRole { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
