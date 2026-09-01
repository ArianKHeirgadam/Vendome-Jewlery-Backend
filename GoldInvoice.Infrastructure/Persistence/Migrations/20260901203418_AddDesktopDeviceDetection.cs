using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoldInvoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDesktopDeviceDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DesktopDevices_RegisteredByUserId",
                schema: "devices",
                table: "DesktopDevices");

            migrationBuilder.AddColumn<string>(
                name: "DeviceType",
                schema: "devices",
                table: "DesktopDevices",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsOnline",
                schema: "devices",
                table: "DesktopDevices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                schema: "devices",
                table: "DesktopDevices",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesktopDevices_RegisteredByUserId_DeviceType_IsOnline",
                schema: "devices",
                table: "DesktopDevices",
                columns: new[] { "RegisteredByUserId", "DeviceType", "IsOnline" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DesktopDevices_Type",
                schema: "devices",
                table: "DesktopDevices",
                sql: "[DeviceType] IN ('Unknown', 'Printer', 'Scanner')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DesktopDevices_RegisteredByUserId_DeviceType_IsOnline",
                schema: "devices",
                table: "DesktopDevices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DesktopDevices_Type",
                schema: "devices",
                table: "DesktopDevices");

            migrationBuilder.DropColumn(
                name: "DeviceType",
                schema: "devices",
                table: "DesktopDevices");

            migrationBuilder.DropColumn(
                name: "IsOnline",
                schema: "devices",
                table: "DesktopDevices");

            migrationBuilder.DropColumn(
                name: "Model",
                schema: "devices",
                table: "DesktopDevices");

            migrationBuilder.CreateIndex(
                name: "IX_DesktopDevices_RegisteredByUserId",
                schema: "devices",
                table: "DesktopDevices",
                column: "RegisteredByUserId");
        }
    }
}
