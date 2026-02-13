using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SellerRatingModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SellerRatings_SellerId_CreatedOn",
                table: "SellerRatings");

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "SellerRatings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FlagReason",
                table: "SellerRatings",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlagged",
                table: "SellerRatings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastModeratedBy",
                table: "SellerRatings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModeratedOn",
                table: "SellerRatings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                table: "SellerRatings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "SellerRatings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.CreateIndex(
                name: "IX_SellerRatings_SellerId_Status_CreatedOn",
                table: "SellerRatings",
                columns: new[] { "SellerId", "Status", "CreatedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SellerRatings_SellerId_Status_CreatedOn",
                table: "SellerRatings");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                table: "SellerRatings");

            migrationBuilder.DropColumn(
                name: "FlagReason",
                table: "SellerRatings");

            migrationBuilder.DropColumn(
                name: "IsFlagged",
                table: "SellerRatings");

            migrationBuilder.DropColumn(
                name: "LastModeratedBy",
                table: "SellerRatings");

            migrationBuilder.DropColumn(
                name: "LastModeratedOn",
                table: "SellerRatings");

            migrationBuilder.DropColumn(
                name: "SellerName",
                table: "SellerRatings");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SellerRatings");

            migrationBuilder.CreateIndex(
                name: "IX_SellerRatings_SellerId_CreatedOn",
                table: "SellerRatings",
                columns: new[] { "SellerId", "CreatedOn" });
        }
    }
}
