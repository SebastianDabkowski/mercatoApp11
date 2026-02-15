using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.Modules.Products.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CategoryAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttributeData",
                table: "ProductModel",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategoryAttributeDefinition",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsDeprecated = table.Column<bool>(type: "bit", nullable: false),
                    Options = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryAttributeDefinition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryAttributeUsage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    DefinitionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryAttributeUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryAttributeUsage_CategoryAttributeDefinition_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "CategoryAttributeDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CategoryAttributeUsage_CategoryModel_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "CategoryModel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryAttributeDefinition_Name_Type",
                table: "CategoryAttributeDefinition",
                columns: new[] { "Name", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryAttributeUsage_CategoryId_DefinitionId",
                table: "CategoryAttributeUsage",
                columns: new[] { "CategoryId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryAttributeUsage_DefinitionId",
                table: "CategoryAttributeUsage",
                column: "DefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryAttributeUsage");

            migrationBuilder.DropTable(
                name: "CategoryAttributeDefinition");

            migrationBuilder.DropColumn(
                name: "AttributeData",
                table: "ProductModel");
        }
    }
}
