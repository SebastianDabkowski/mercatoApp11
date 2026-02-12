using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerVerificationDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompanyRegistrationNumber",
                table: "AspNetUsers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonalIdNumber",
                table: "AspNetUsers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerType",
                table: "AspNetUsers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Individual");

            migrationBuilder.AddColumn<string>(
                name: "VerificationContactName",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyRegistrationNumber",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PersonalIdNumber",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SellerType",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "VerificationContactName",
                table: "AspNetUsers");
        }
    }
}
