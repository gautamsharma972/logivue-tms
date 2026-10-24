using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tms.SharedKernel.Security;

namespace Tms.Modules.TransportOps.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>. Override the target with the TMS_DESIGN_CONNECTION environment variable.</summary>
internal sealed class TransportOpsDbContextFactory : IDesignTimeDbContextFactory<TransportOpsDbContext>
{
    public TransportOpsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("TMS_DESIGN_CONNECTION")
            ?? "Server=localhost;Port=3306;Database=tms_dev;User=tms_app;Password=tms_dev_password;";

        var builder = new DbContextOptionsBuilder<TransportOpsDbContext>();
        TransportOpsDbContextOptions.Configure(builder, connection, new Version(8, 4, 0));
        return new TransportOpsDbContext(builder.Options, SystemUser.Instance);
    }
}
