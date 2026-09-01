using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoldInvoice.Infrastructure.Integration;

internal sealed class ReadCommittedConnectionInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        InterceptionResult result)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;";
        await command.ExecuteNonQueryAsync(CancellationToken.None);

        await base.ConnectionOpenedAsync(connection, eventData, result);
    }
}
