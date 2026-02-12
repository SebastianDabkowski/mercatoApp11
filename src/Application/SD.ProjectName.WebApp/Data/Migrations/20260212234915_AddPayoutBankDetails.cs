using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutBankDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayoutBankAccount",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutBankRouting",
                table: "AspNetUsers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PayoutUpdatedOn",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayoutBankAccount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PayoutBankRouting",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PayoutUpdatedOn",
                table: "AspNetUsers");
        }
    }
}
