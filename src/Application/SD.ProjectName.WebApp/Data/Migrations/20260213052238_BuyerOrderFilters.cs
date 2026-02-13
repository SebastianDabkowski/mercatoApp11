using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class BuyerOrderFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_BuyerId_CreatedOn",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BuyerId_Status_CreatedOn",
                table: "Orders",
                columns: new[] { "BuyerId", "Status", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedOn",
                table: "Orders",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_BuyerId_Status_CreatedOn",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedOn",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BuyerId_CreatedOn",
                table: "Orders",
                columns: new[] { "BuyerId", "CreatedOn" });
        }
    }
}
