using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SellerRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SellerRatings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerRatings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerRatings_OrderId_SellerId_BuyerId",
                table: "SellerRatings",
                columns: new[] { "OrderId", "SellerId", "BuyerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerRatings_SellerId_CreatedOn",
                table: "SellerRatings",
                columns: new[] { "SellerId", "CreatedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerRatings");
        }
    }
}
