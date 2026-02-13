using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShippingAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayoutSchedule",
                table: "AspNetUsers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Weekly");

            migrationBuilder.AddColumn<string>(
                name: "SavedAddressKey",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShippingAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Line1 = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Line2 = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    City = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: ""),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingAddresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingAddresses_UserId_CreatedOn",
                table: "ShippingAddresses",
                columns: new[] { "UserId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingAddresses_UserId_IsDefault",
                table: "ShippingAddresses",
                columns: new[] { "UserId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShippingAddresses");

            migrationBuilder.DropColumn(
                name: "SavedAddressKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayoutSchedule",
                table: "AspNetUsers");
        }
    }
}
