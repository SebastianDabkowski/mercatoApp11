using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SellerOnboardingWizard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingCompletedOn",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingStartedOn",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnboardingStatus",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.AddColumn<int>(
                name: "OnboardingStep",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PayoutAccount",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PayoutMethod",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "BankTransfer");

            migrationBuilder.AddColumn<string>(
                name: "StoreDescription",
                table: "AspNetUsers",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnboardingCompletedOn",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OnboardingStartedOn",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OnboardingStatus",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OnboardingStep",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PayoutAccount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PayoutMethod",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "StoreDescription",
                table: "AspNetUsers");
        }
    }
}
