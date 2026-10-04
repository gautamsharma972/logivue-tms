using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tms.SharedKernel.Persistence;

/// <summary>Stores an immutable value (a record, a list of records, a polymorphic hierarchy) in one JSON column.</summary>
public static class JsonColumn
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static PropertyBuilder<T> HasJsonValue<T>(this PropertyBuilder<T> property)
        where T : class?
    {
        var converter = new ValueConverter<T, string>(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<T>(v, Options)!);

        // Compare and snapshot by serialised form so change detection works for records and collections alike.
        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!);

        property.HasColumnType("json").HasConversion(converter);
        property.Metadata.SetValueComparer(comparer);
        return property;
    }
}
