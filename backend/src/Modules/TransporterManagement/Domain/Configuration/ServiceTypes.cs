namespace LogiVue.Tms.TransporterManagement.Domain.Configuration;

/// <summary>A service type that lanes and tenders may use, such as FTL or EXPRESS. Managed as data, not code.</summary>
public class ServiceTypeDefinition
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
