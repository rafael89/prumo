using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Prumo.Infrastructure.Persistence;

public static class PostgresTransientFailure
{
    public static bool IsDeadlock(Exception exception)
        => exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected };

    public static async Task SaveChangesWithDeadlockRetryAsync(DbContext db, CancellationToken cancellationToken, int maxAttempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (IsDeadlock(exception) && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(75 * attempt), cancellationToken);
            }
        }
    }
}
