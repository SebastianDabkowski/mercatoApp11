using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.Modules.Products.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CategorySlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "CategoryModel",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "CategoryModel",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE CategoryModel
                SET Slug = LOWER(REPLACE(REPLACE(TRIM(Name), ' ', '-'), '/', '-'))
                WHERE Slug IS NULL OR Slug = ''
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryModel_ParentId_Slug",
                table: "CategoryModel",
                columns: new[] { "ParentId", "Slug" },
                unique: true,
                filter: "[ParentId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CategoryModel_ParentId_Slug",
                table: "CategoryModel");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "CategoryModel");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "CategoryModel");
        }
    }
}
