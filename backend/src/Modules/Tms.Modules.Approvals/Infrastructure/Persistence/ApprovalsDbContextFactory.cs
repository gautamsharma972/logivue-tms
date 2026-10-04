using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Approvals.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>. Override the target with the TMS_DESIGN_CONNECTION environment variable.</summary>
internal sealed class ApprovalsDbContextFactory : IDesignTimeDbContextFactory<ApprovalsDbContext>
{
    public ApprovalsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("TMS_DESIGN_CONNECTION")
            ?? "Server=localhost;Port=3306;Database=tms_dev;User=tms_app;Password=tms_dev_password;";

        var builder = new DbContextOptionsBuilder<ApprovalsDbContext>();
        ApprovalsDbContextOptions.Configure(builder, connection, new Version(8, 4, 0));
        return new ApprovalsDbContext(builder.Options, SystemUser.Instance);
    }
}
