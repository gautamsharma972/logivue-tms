using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tms.Modules.Approvals.Infrastructure.Persistence;

internal static class JsonListMapping
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Stores an immutable list of records as one JSON column. Keeps the aggregate in one row and makes the audit
    /// trail show a single readable before/after, instead of per-child rows.
    /// </summary>
    public static PropertyBuilder<IReadOnlyList<T>> HasJsonList<T>(this PropertyBuilder<IReadOnlyList<T>> property)
    {
        var converter = new ValueConverter<IReadOnlyList<T>, string>(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<List<T>>(v, Options) ?? new List<T>());

        var comparer = new ValueComparer<IReadOnlyList<T>>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, Options), Options)!);

        property.HasColumnType("json").HasConversion(converter);
        property.Metadata.SetValueComparer(comparer);
        return property;
    }
}
