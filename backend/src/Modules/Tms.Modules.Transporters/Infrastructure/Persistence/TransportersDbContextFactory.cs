using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>. Override the target with the TMS_DESIGN_CONNECTION environment variable.</summary>
internal sealed class TransportersDbContextFactory : IDesignTimeDbContextFactory<TransportersDbContext>
{
    public TransportersDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("TMS_DESIGN_CONNECTION")
            ?? "Server=localhost;Port=3306;Database=tms_dev;User=tms_app;Password=tms_dev_password;";

        var builder = new DbContextOptionsBuilder<TransportersDbContext>();
        TransportersDbContextOptions.Configure(builder, connection, new Version(8, 4, 0));
        // Design-time only: a throwaway key so the model can be built; never used for real data.
        var encryptor = new AesGcmFieldEncryptor(Microsoft.Extensions.Options.Options.Create(new FieldEncryptionOptions
        {
            Keys = { ["1"] = Convert.ToBase64String(new byte[32]) },
        }));
        return new TransportersDbContext(builder.Options, SystemUser.Instance, encryptor);
    }
}
