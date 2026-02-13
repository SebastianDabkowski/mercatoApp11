using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.Modules.Products.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductWorkflowState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ProductModel",
                newName: "Title");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ProductModel",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "SellerId",
                table: "ProductModel",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ProductModel",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Stock",
                table: "ProductModel",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowState",
                table: "ProductModel",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "draft");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "SellerId",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "Stock",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "WorkflowState",
                table: "ProductModel");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "ProductModel",
                newName: "Name");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ProductModel",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }
    }
}
