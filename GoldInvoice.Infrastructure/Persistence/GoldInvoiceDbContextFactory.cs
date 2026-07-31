using GoldInvoice.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoldInvoice.Infrastructure.Persistence;

public sealed class GoldInvoiceDbContextFactory : IDesignTimeDbContextFactory<GoldInvoiceDbContext>
{
    public GoldInvoiceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__GoldInvoice");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=VendomeGoldInvoiceDesignTime;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";
        }

        var builder = new DbContextOptionsBuilder<GoldInvoiceDbContext>();
        builder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(GoldInvoiceDbContext).Assembly.FullName));
        builder.AddInterceptors(new AuditingSaveChangesInterceptor(TimeProvider.System));

        return new GoldInvoiceDbContext(builder.Options);
    }
}
