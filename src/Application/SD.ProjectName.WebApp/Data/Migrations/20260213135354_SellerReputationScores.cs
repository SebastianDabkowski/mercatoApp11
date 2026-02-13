using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SellerReputationScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SellerReputations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Score = table.Column<double>(type: "float", nullable: false),
                    RatingAverage = table.Column<double>(type: "float", nullable: false),
                    RatedOrderCount = table.Column<int>(type: "int", nullable: false),
                    DisputeRate = table.Column<double>(type: "float", nullable: false),
                    OnTimeShippingRate = table.Column<double>(type: "float", nullable: false),
                    CancellationRate = table.Column<double>(type: "float", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CalculatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerReputations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerReputations_SellerId",
                table: "SellerReputations",
                column: "SellerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerReputations");
        }
    }
}
