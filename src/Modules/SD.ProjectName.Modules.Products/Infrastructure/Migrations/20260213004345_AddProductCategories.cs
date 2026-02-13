using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.Modules.Products.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "ProductModel",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "ProductModel",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategoryModel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    FullPath = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryModel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryModel_CategoryModel_ParentId",
                        column: x => x.ParentId,
                        principalTable: "CategoryModel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductModel_CategoryId",
                table: "ProductModel",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryModel_ParentId_SortOrder",
                table: "CategoryModel",
                columns: new[] { "ParentId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProductModel_CategoryModel_CategoryId",
                table: "ProductModel",
                column: "CategoryId",
                principalTable: "CategoryModel",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductModel_CategoryModel_CategoryId",
                table: "ProductModel");

            migrationBuilder.DropTable(
                name: "CategoryModel");

            migrationBuilder.DropIndex(
                name: "IX_ProductModel_CategoryId",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "ProductModel");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "ProductModel",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);
        }
    }
}
