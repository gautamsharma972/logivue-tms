using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Tms.Modules.Platform.Application;

internal static class PersistenceErrors
{
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };
}
