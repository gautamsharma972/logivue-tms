using System.Text.Json;

namespace LogiVue.Tms.TransporterManagement.Application.Common;

/// <summary>Serialises entity snapshots for audit old/new values.</summary>
internal static class AuditJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
