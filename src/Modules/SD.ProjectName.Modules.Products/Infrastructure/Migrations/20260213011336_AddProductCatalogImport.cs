using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.Modules.Products.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCatalogImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SellerId",
                table: "ProductModel",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "MerchantSku",
                table: "ProductModel",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE ProductModel SET MerchantSku = CONCAT('SKU-', Id) WHERE MerchantSku = '' OR MerchantSku IS NULL;");

            migrationBuilder.CreateTable(
                name: "ProductImportJob",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SellerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    CreatedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ErrorReport = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FileContent = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TemplateVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "v1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImportJob", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductModel_SellerId_MerchantSku",
                table: "ProductModel",
                columns: new[] { "SellerId", "MerchantSku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductImportJob_SellerId_CreatedOn",
                table: "ProductImportJob",
                columns: new[] { "SellerId", "CreatedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductImportJob");

            migrationBuilder.DropIndex(
                name: "IX_ProductModel_SellerId_MerchantSku",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "MerchantSku",
                table: "ProductModel");

            migrationBuilder.AlterColumn<string>(
                name: "SellerId",
                table: "ProductModel",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
