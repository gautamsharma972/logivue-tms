using Tms.Modules.Platform;
using Tms.Modules.Platform.Application;

namespace Tms.Api;

/// <summary>
/// <c>dotnet Tms.Api.dll tenant:create --code ACME --name "Acme Freight Ltd" --admin-email a@acme.com --admin-name "A. Admin"</c>
/// Creates a customer organisation and prints a one-time link for its first administrator to choose a password.
/// </summary>
internal static class TenantCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        string? Arg(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var code = Arg("--code");
        var name = Arg("--name");
        var email = Arg("--admin-email");
        var adminName = Arg("--admin-name");
        if (code is null || name is null || email is null || adminName is null)
        {
            await Console.Error.WriteLineAsync("Usage: tenant:create --code <CODE> --name <organisation name> --admin-email <email> --admin-name <name>");
            return 2;
        }

        var result = await services.ProvisionTenantAsync(new ProvisionTenantRequest(code, name, email, adminName));
        if (result.IsFailure)
        {
            await Console.Error.WriteLineAsync($"Failed: {result.Error.Description}");
            return 1;
        }

        var tenant = result.Value;
        Console.WriteLine($"Created organisation {tenant.Code} ({tenant.TenantId}).");
        Console.WriteLine($"Administrator: {tenant.AdminEmail}");
        Console.WriteLine($"Set-password link (single use, expires {tenant.InviteExpiresAt:u}):");
        Console.WriteLine(tenant.InviteLink);
        return 0;
    }
}
