using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoldInvoice.Infrastructure.Integration;

internal sealed class ReadCommittedConnectionInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;";
        await command.ExecuteNonQueryAsync(cancellationToken);

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }
}
