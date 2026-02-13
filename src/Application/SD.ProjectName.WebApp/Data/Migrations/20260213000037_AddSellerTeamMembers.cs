using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerTeamMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoreOwnerId",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SellerTeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreOwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InvitationCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InvitedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AcceptedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeactivatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerTeamMembers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_StoreOwnerId",
                table: "AspNetUsers",
                column: "StoreOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerTeamMembers_InvitationCode",
                table: "SellerTeamMembers",
                column: "InvitationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerTeamMembers_StoreOwnerId_Email",
                table: "SellerTeamMembers",
                columns: new[] { "StoreOwnerId", "Email" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerTeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_StoreOwnerId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "StoreOwnerId",
                table: "AspNetUsers");
        }
    }
}
