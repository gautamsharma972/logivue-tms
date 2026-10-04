using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Platform.Application.Users;

public sealed record UserLookupDto(Guid Id, string FullName, string Email);

/// <summary>Minimal people-picker data (name + email of active colleagues) for any signed-in user, e.g. choosing a delegate.</summary>
internal sealed class UserLookupHandler(PlatformDbContext db)
{
    public async Task<Result<IReadOnlyList<UserLookupDto>>> HandleAsync(string? search, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking().Where(u => u.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            users = users.Where(u => u.FullName.Contains(term) || u.Email.Contains(term));
        }

        var rows = await users.OrderBy(u => u.FullName).Take(20)
            .Select(u => new UserLookupDto(u.Id, u.FullName, u.Email))
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<UserLookupDto>>(rows);
    }
}
