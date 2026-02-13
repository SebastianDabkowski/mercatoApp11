using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.Modules.Products.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCatalogExport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductExportJob",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Search = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WorkflowState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    UseFilters = table.Column<bool>(type: "bit", nullable: false),
                    TotalProducts = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FileContent = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductExportJob", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductExportJob_SellerId_CreatedOn",
                table: "ProductExportJob",
                columns: new[] { "SellerId", "CreatedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductExportJob");
        }
    }
}
