using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Application;

internal static class Support
{
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };

    /// <summary>The name of the index that rejected the write, so the caller can say which field clashed.</summary>
    public static string ViolatedIndex(this DbUpdateException exception) => exception.InnerException?.Message ?? string.Empty;

    /// <summary>"Today" for expiry purposes is the Indian calendar date (IST, no daylight saving), whatever the server's zone.</summary>
    public static DateOnly TodayInIndia(this TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    public static async Task<Result<Transporter>> FindTransporterAsync(
        this TransportersDbContext db,
        TransporterAccess access,
        Guid id,
        AccessLevel level,
        CancellationToken cancellationToken)
    {
        var allowed = access.Check(id, level);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var transporter = await db.Transporters.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return transporter is null ? TransporterAccess.NotFound : transporter;
    }

    /// <summary>Kinds of current (non-superseded) transporter-level documents on file.</summary>
    public static async Task<List<DocumentKind>> TransporterDocumentKindsAsync(this TransportersDbContext db, Guid transporterId, CancellationToken cancellationToken) =>
        await db.Documents.AsNoTracking()
            .Where(d => d.OwnerKind == OwnerKind.Transporter && d.OwnerId == transporterId && d.SupersededAt == null)
            .Select(d => d.Kind)
            .Distinct()
            .ToListAsync(cancellationToken);
}
