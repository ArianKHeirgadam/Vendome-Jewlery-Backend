using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoldInvoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleVerifiedPaymentPerOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_OrderId",
                schema: "billing",
                table: "Payments");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                schema: "billing",
                table: "Payments",
                column: "OrderId",
                unique: true,
                filter: "[Status] IN ('Pending', 'Processing', 'RequiresReview', 'Verified')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_OrderId",
                schema: "billing",
                table: "Payments");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                schema: "billing",
                table: "Payments",
                column: "OrderId",
                unique: true,
                filter: "[Status] IN ('Pending', 'Processing', 'RequiresReview')");
        }
    }
}
