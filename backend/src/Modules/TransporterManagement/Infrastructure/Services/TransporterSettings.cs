using System.Text.Json;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Services;

/// <summary>Reads business configuration from <c>tm_configuration_settings</c>.</summary>
internal sealed class TransporterSettings(TransporterDbContext db) : ITransporterSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var json = await db.ConfigurationSettings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.ValueJson)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Configuration setting '{key}' is not defined.");

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Configuration setting '{key}' could not be read as {typeof(T).Name}.");
    }
}
