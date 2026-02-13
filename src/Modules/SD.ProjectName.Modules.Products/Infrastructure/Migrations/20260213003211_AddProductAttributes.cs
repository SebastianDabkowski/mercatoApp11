using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.Modules.Products.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GalleryImageUrls",
                table: "ProductModel",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                table: "ProductModel",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthCm",
                table: "ProductModel",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MainImageUrl",
                table: "ProductModel",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingMethods",
                table: "ProductModel",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "ProductModel",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WidthCm",
                table: "ProductModel",
                type: "decimal(18,3)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GalleryImageUrls",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "LengthCm",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "MainImageUrl",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "ShippingMethods",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "WidthCm",
                table: "ProductModel");
        }
    }
}
