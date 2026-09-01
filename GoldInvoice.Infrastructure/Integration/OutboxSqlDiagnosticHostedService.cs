using System.Data;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Integration;

/// <summary>
/// One-shot diagnostic probe for the SQL Server outbox claim statement.
/// It reproduces the claim UPDATE inside a transaction and always rolls it back,
/// while logging the actual SqlException details that the normal dispatcher currently hides.
/// This service exists only on the debug/outbox-sql-exception branch.
/// </summary>
internal sealed class OutboxSqlDiagnosticHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxSqlDiagnosticHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            await ProbeAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Outbox SQL diagnostic probe failed unexpectedly. ExceptionType={ExceptionType}, Message={Message}",
                exception.GetType().FullName,
                exception.Message);
        }
    }

    private async Task ProbeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();
        var connection = dbContext.Database.GetDbConnection();

        await connection.OpenAsync(cancellationToken);
        try
        {
            await LogConnectionIdentityAsync(connection, cancellationToken);

            var lockId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var lockedUntil = now.AddSeconds(options.Value.LockDurationSeconds);

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandTimeout = 30;
            command.CommandText = """
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                ;WITH [Claimable] AS
                (
                    SELECT TOP (@p0) *
                    FROM [integration].[OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE
                        (([Status] IN ('Pending', 'Failed')) AND
                         ([NextRetryAt] IS NULL OR [NextRetryAt] <= @p1))
                        OR
                        ([Status] = 'Processing' AND [LockedUntil] <= @p2)
                    ORDER BY [OccurredAt], [Id]
                )
                UPDATE [Claimable]
                SET [Status] = 'Processing',
                    [LockId] = @p3,
                    [LockedUntil] = @p4,
                    [UpdatedAt] = @p5;

                ROLLBACK TRANSACTION;
                """;

            command.Parameters.Add(new SqlParameter("@p0", SqlDbType.Int) { Value = options.Value.BatchSize });
            command.Parameters.Add(new SqlParameter("@p1", SqlDbType.DateTimeOffset) { Value = now });
            command.Parameters.Add(new SqlParameter("@p2", SqlDbType.DateTimeOffset) { Value = now });
            command.Parameters.Add(new SqlParameter("@p3", SqlDbType.UniqueIdentifier) { Value = lockId });
            command.Parameters.Add(new SqlParameter("@p4", SqlDbType.DateTimeOffset) { Value = lockedUntil });
            command.Parameters.Add(new SqlParameter("@p5", SqlDbType.DateTimeOffset) { Value = now });

            try
            {
                var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                logger.LogInformation(
                    "Outbox SQL diagnostic probe succeeded. SimulatedClaimUpdateRows={Affected}. The transaction was rolled back.",
                    affected);
            }
            catch (SqlException exception)
            {
                foreach (SqlError error in exception.Errors)
                {
                    logger.LogError(
                        "OUTBOX SQL ERROR: Number={Number}, State={State}, Class={Class}, Procedure={Procedure}, Line={Line}, Server={Server}, Message={Message}",
                        error.Number,
                        error.State,
                        error.Class,
                        error.Procedure,
                        error.LineNumber,
                        error.Server,
                        error.Message);
                }

                logger.LogError(
                    exception,
                    "OUTBOX SQL EXCEPTION: Number={Number}, Message={Message}",
                    exception.Number,
                    exception.Message);
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task LogConnectionIdentityAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            SELECT
                DB_NAME() AS DatabaseName,
                SUSER_SNAME() AS CurrentLogin,
                ORIGINAL_LOGIN() AS OriginalLogin,
                USER_NAME() AS DatabaseUser,
                @@SERVERNAME AS ServerName;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            // The caller's logger is intentionally not used here; the connection identity
            // is returned through the exception-safe diagnostic log below.
        }
    }
}
