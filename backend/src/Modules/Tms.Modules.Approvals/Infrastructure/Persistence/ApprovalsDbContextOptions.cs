using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Tms.Modules.Approvals.Infrastructure.Persistence;

internal static class ApprovalsDbContextOptions
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString, Version serverVersion) =>
        options
            .UseMySql(connectionString, new MySqlServerVersion(serverVersion), mysql =>
            {
                mysql.MigrationsHistoryTable("migrations_history", ApprovalsDbContext.Schema);
                mysql.SchemaBehavior(MySqlSchemaBehavior.Translate, (schema, table) => $"{schema}_{table}");
            })
            .UseSnakeCaseNamingConvention();
}
