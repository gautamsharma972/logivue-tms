using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tms.Modules.TransportOps.Infrastructure.Persistence;

internal static class JsonMapping
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Stores a value (a record, or a list of records) as one JSON column, so the aggregate stays in one row and the audit trail shows one readable before/after.</summary>
    public static PropertyBuilder<T> HasJson<T>(this PropertyBuilder<T> property, string? emptyDefault = null)
    {
        var converter = new ValueConverter<T, string>(v => JsonSerializer.Serialize(v, Options), v => JsonSerializer.Deserialize<T>(v, Options)!);
        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!);
        property.HasColumnType("json").HasConversion(converter);
        property.Metadata.SetValueComparer(comparer);
        if (emptyDefault is not null)
        {
            property.HasDefaultValueSql($"({emptyDefault})");
        }

        return property;
    }
}
