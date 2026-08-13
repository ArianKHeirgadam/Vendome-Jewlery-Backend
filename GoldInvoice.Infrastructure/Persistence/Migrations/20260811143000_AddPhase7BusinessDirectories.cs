using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoldInvoice.Infrastructure.Persistence.Migrations;

[DbContext(typeof(GoldInvoiceDbContext))]
[Migration("20260811143000_AddPhase7BusinessDirectories")]
public sealed class AddPhase7BusinessDirectories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "business");
        migrationBuilder.EnsureSchema(name: "crm");

        migrationBuilder.CreateTable(
            name: "Suppliers",
            schema: "business",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                PhoneNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                Email = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: true),
                NationalId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                AddressLine = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Suppliers", x => x.Id);
                table.CheckConstraint(
                    "CK_Suppliers_SoftDelete",
                    "([IsDeleted] = 0 AND [DeletedAt] IS NULL) OR ([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL)");
            });

        migrationBuilder.CreateTable(
            name: "CustomerInteractions",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InteractionType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                NextFollowUpAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                Status = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerInteractions", x => x.Id);
                table.CheckConstraint(
                    "CK_CustomerInteractions_Completion",
                    "([Status] = 'Completed' AND [CompletedAt] IS NOT NULL) OR ([Status] <> 'Completed' AND [CompletedAt] IS NULL)");
                table.CheckConstraint(
                    "CK_CustomerInteractions_FollowUp",
                    "[NextFollowUpAt] IS NULL OR [NextFollowUpAt] > [OccurredAt]");
                table.ForeignKey(
                    name: "FK_CustomerInteractions_Users_CustomerId",
                    column: x => x.CustomerId,
                    principalSchema: "security",
                    principalTable: "Users",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_Suppliers_Code",
            schema: "business",
            table: "Suppliers",
            column: "Code",
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_Suppliers_IsActive_Name",
            schema: "business",
            table: "Suppliers",
            columns: new[] { "IsActive", "Name" });

        migrationBuilder.CreateIndex(
            name: "IX_CustomerInteractions_CustomerId_OccurredAt",
            schema: "crm",
            table: "CustomerInteractions",
            columns: new[] { "CustomerId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_CustomerInteractions_Status_NextFollowUpAt",
            schema: "crm",
            table: "CustomerInteractions",
            columns: new[] { "Status", "NextFollowUpAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CustomerInteractions", schema: "crm");
        migrationBuilder.DropTable(name: "Suppliers", schema: "business");
    }
}
