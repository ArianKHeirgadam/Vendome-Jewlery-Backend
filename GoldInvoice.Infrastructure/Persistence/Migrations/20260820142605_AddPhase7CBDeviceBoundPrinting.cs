using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoldInvoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase7CBDeviceBoundPrinting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DesktopDevices_Revocation",
                schema: "devices",
                table: "DesktopDevices");

            migrationBuilder.AddColumn<Guid>(
                name: "PrintJobId",
                schema: "invoicing",
                table: "InvoicePrintLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                schema: "devices",
                table: "DesktopDevices",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                schema: "devices",
                table: "DesktopDevices",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicKeyPem",
                schema: "devices",
                table: "DesktopDevices",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [devices].[DesktopDevices] SET [ApprovedAt] = [CreatedAt] WHERE [IsActive] = 1 AND [ApprovedAt] IS NULL;");

            migrationBuilder.CreateTable(
                name: "DevicePrinters",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DesktopDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SystemPrinterName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PrinterType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevicePrinters", x => x.Id);
                    table.CheckConstraint("CK_DevicePrinters_Default", "([IsDefault] = 0) OR ([IsDefault] = 1 AND [IsEnabled] = 1)");
                    table.ForeignKey(
                        name: "FK_DevicePrinters_DesktopDevices_DesktopDeviceId",
                        column: x => x.DesktopDeviceId,
                        principalSchema: "devices",
                        principalTable: "DesktopDevices",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeviceRegistrationTokens",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenValueHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceRegistrationTokens", x => x.Id);
                    table.CheckConstraint("CK_DeviceRegistrationTokens_Expiry", "[ExpiresAt] > [CreatedAt]");
                    table.CheckConstraint("CK_DeviceRegistrationTokens_Use", "([UsedAt] IS NULL) OR ([UsedAt] IS NOT NULL AND [ExpiresAt] > [UsedAt])");
                    table.ForeignKey(
                        name: "FK_DeviceRegistrationTokens_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PrintProfiles",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DesktopDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PaperSize = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Orientation = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Copies = table.Column<int>(type: "int", nullable: false),
                    ColorMode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    MarginLeftMillimeters = table.Column<int>(type: "int", nullable: false),
                    MarginRightMillimeters = table.Column<int>(type: "int", nullable: false),
                    MarginTopMillimeters = table.Column<int>(type: "int", nullable: false),
                    MarginBottomMillimeters = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintProfiles", x => x.Id);
                    table.CheckConstraint("CK_PrintProfiles_Copies", "[Copies] BETWEEN 1 AND 20");
                    table.CheckConstraint("CK_PrintProfiles_Default", "([IsDefault] = 0) OR ([IsDefault] = 1 AND [IsEnabled] = 1)");
                    table.CheckConstraint("CK_PrintProfiles_Margins", "[MarginLeftMillimeters] BETWEEN 0 AND 1000 AND [MarginRightMillimeters] BETWEEN 0 AND 1000 AND [MarginTopMillimeters] BETWEEN 0 AND 1000 AND [MarginBottomMillimeters] BETWEEN 0 AND 1000");
                    table.ForeignKey(
                        name: "FK_PrintProfiles_DesktopDevices_DesktopDeviceId",
                        column: x => x.DesktopDeviceId,
                        principalSchema: "devices",
                        principalTable: "DesktopDevices",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoicePrintJobs",
                schema: "invoicing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DesktopDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DevicePrinterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrintProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Copies = table.Column<int>(type: "int", nullable: false),
                    IsReprint = table.Column<bool>(type: "bit", nullable: false),
                    ReprintReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    PrintedAtPrinterName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PrintedByAgentSignature = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePrintJobs", x => x.Id);
                    table.CheckConstraint("CK_InvoicePrintJobs_Completion", "([Status] = 'Requested' AND [CompletedAt] IS NULL AND [FailureCode] IS NULL AND [PrintedAtPrinterName] IS NULL AND [PrintedByAgentSignature] IS NULL) OR ([Status] = 'Succeeded' AND [CompletedAt] IS NOT NULL AND [PrintedAtPrinterName] IS NOT NULL AND [PrintedByAgentSignature] IS NOT NULL) OR ([Status] = 'Failed' AND [CompletedAt] IS NOT NULL AND [FailureCode] IS NOT NULL AND [PrintedAtPrinterName] IS NULL AND [PrintedByAgentSignature] IS NULL)");
                    table.CheckConstraint("CK_InvoicePrintJobs_Copies", "[Copies] > 0");
                    table.CheckConstraint("CK_InvoicePrintJobs_RetryCount", "[RetryCount] >= 0");
                    table.CheckConstraint("CK_InvoicePrintJobs_Status", "[Status] IN ('Requested', 'Succeeded', 'Failed')");
                    table.ForeignKey(
                        name: "FK_InvoicePrintJobs_DesktopDevices_DesktopDeviceId",
                        column: x => x.DesktopDeviceId,
                        principalSchema: "devices",
                        principalTable: "DesktopDevices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvoicePrintJobs_DevicePrinters_DevicePrinterId",
                        column: x => x.DevicePrinterId,
                        principalSchema: "devices",
                        principalTable: "DevicePrinters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvoicePrintJobs_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "invoicing",
                        principalTable: "Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvoicePrintJobs_PrintProfiles_PrintProfileId",
                        column: x => x.PrintProfileId,
                        principalSchema: "devices",
                        principalTable: "PrintProfiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvoicePrintJobs_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePrintLogs_PrintJobId",
                schema: "invoicing",
                table: "InvoicePrintLogs",
                column: "PrintJobId",
                filter: "[PrintJobId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoicePrintLogs_DeviceBinding",
                schema: "invoicing",
                table: "InvoicePrintLogs",
                sql: "([PrintJobId] IS NULL AND [DesktopDeviceId] IS NULL) OR ([PrintJobId] IS NOT NULL AND [DesktopDeviceId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DesktopDevices_State",
                schema: "devices",
                table: "DesktopDevices",
                sql: "([IsActive] = 1 AND [RevokedAt] IS NULL AND [ApprovedAt] IS NOT NULL) OR ([IsActive] = 0 AND [RevokedAt] IS NOT NULL) OR ([IsActive] = 0 AND [RevokedAt] IS NULL AND [ApprovedAt] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_DevicePrinters_DesktopDeviceId_IsDefault",
                schema: "devices",
                table: "DevicePrinters",
                columns: new[] { "DesktopDeviceId", "IsDefault" },
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DevicePrinters_DesktopDeviceId_SystemPrinterName",
                schema: "devices",
                table: "DevicePrinters",
                columns: new[] { "DesktopDeviceId", "SystemPrinterName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRegistrationTokens_CreatedById",
                schema: "devices",
                table: "DeviceRegistrationTokens",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRegistrationTokens_ExpiresAt_UsedAt",
                schema: "devices",
                table: "DeviceRegistrationTokens",
                columns: new[] { "ExpiresAt", "UsedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRegistrationTokens_TokenValueHash",
                schema: "devices",
                table: "DeviceRegistrationTokens",
                column: "TokenValueHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePrintJobs_DesktopDeviceId_Status_CreatedAt",
                schema: "invoicing",
                table: "InvoicePrintJobs",
                columns: new[] { "DesktopDeviceId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePrintJobs_DesktopDeviceId_Status_IdempotencyKeyHash",
                schema: "invoicing",
                table: "InvoicePrintJobs",
                columns: new[] { "DesktopDeviceId", "Status", "IdempotencyKeyHash" },
                unique: true,
                filter: "[IdempotencyKeyHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePrintJobs_DevicePrinterId",
                schema: "invoicing",
                table: "InvoicePrintJobs",
                column: "DevicePrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePrintJobs_InvoiceId_CreatedAt",
                schema: "invoicing",
                table: "InvoicePrintJobs",
                columns: new[] { "InvoiceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePrintJobs_PrintProfileId",
                schema: "invoicing",
                table: "InvoicePrintJobs",
                column: "PrintProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePrintJobs_RequestedByUserId",
                schema: "invoicing",
                table: "InvoicePrintJobs",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintProfiles_DesktopDeviceId_IsDefault",
                schema: "devices",
                table: "PrintProfiles",
                columns: new[] { "DesktopDeviceId", "IsDefault" },
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PrintProfiles_DesktopDeviceId_Name",
                schema: "devices",
                table: "PrintProfiles",
                columns: new[] { "DesktopDeviceId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoicePrintLogs_InvoicePrintJobs_PrintJobId",
                schema: "invoicing",
                table: "InvoicePrintLogs",
                column: "PrintJobId",
                principalSchema: "invoicing",
                principalTable: "InvoicePrintJobs",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoicePrintLogs_InvoicePrintJobs_PrintJobId",
                schema: "invoicing",
                table: "InvoicePrintLogs");

            migrationBuilder.DropTable(
                name: "DeviceRegistrationTokens",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "InvoicePrintJobs",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "DevicePrinters",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "PrintProfiles",
                schema: "devices");

            migrationBuilder.DropIndex(
                name: "IX_InvoicePrintLogs_PrintJobId",
                schema: "invoicing",
                table: "InvoicePrintLogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoicePrintLogs_DeviceBinding",
                schema: "invoicing",
                table: "InvoicePrintLogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DesktopDevices_State",
                schema: "devices",
                table: "DesktopDevices");

            migrationBuilder.DropColumn(
                name: "PrintJobId",
                schema: "invoicing",
                table: "InvoicePrintLogs");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "devices",
                table: "DesktopDevices");

            migrationBuilder.DropColumn(
                name: "PublicKeyPem",
                schema: "devices",
                table: "DesktopDevices");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                schema: "devices",
                table: "DesktopDevices",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_DesktopDevices_Revocation",
                schema: "devices",
                table: "DesktopDevices",
                sql: "([IsActive] = 1 AND [RevokedAt] IS NULL) OR ([IsActive] = 0 AND [RevokedAt] IS NOT NULL)");
        }
    }
}
