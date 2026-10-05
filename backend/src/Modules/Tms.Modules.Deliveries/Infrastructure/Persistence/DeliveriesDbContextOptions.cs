using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Tms.Modules.Deliveries.Infrastructure.Persistence;

internal static class DeliveriesDbContextOptions
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString, Version serverVersion) =>
        options
            .UseMySql(connectionString, new MySqlServerVersion(serverVersion), mysql =>
            {
                mysql.MigrationsHistoryTable("migrations_history", DeliveriesDbContext.Schema);
                mysql.SchemaBehavior(MySqlSchemaBehavior.Translate, (schema, table) => $"{schema}_{table}");
            })
            .UseSnakeCaseNamingConvention();
}
