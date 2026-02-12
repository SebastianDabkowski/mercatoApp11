using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactWebsite",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreLogoPath",
                table: "AspNetUsers",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_BusinessName",
                table: "AspNetUsers",
                column: "BusinessName",
                unique: true,
                filter: "[BusinessName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_BusinessName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ContactWebsite",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "StoreLogoPath",
                table: "AspNetUsers");
        }
    }
}
