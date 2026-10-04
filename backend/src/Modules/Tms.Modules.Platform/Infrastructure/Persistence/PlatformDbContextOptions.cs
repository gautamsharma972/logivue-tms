using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Tms.Modules.Platform.Infrastructure.Persistence;

internal static class PlatformDbContextOptions
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString, Version serverVersion) =>
        options
            .UseMySql(connectionString, new MySqlServerVersion(serverVersion), mysql =>
            {
                mysql.MigrationsHistoryTable("migrations_history", PlatformDbContext.Schema);

                // MySQL has no schemas: "platform" + "users" becomes the table platform_users.
                mysql.SchemaBehavior(MySqlSchemaBehavior.Translate, (schema, table) => $"{schema}_{table}");
            })
            .UseSnakeCaseNamingConvention();
}
