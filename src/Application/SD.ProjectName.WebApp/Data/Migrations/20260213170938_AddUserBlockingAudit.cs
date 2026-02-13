using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBlockingAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockReason",
                table: "AspNetUsers",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockedByName",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockedByUserId",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BlockedOn",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserAdminAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAdminAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAdminAudits_UserId",
                table: "UserAdminAudits",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAdminAudits_UserId_CreatedOn",
                table: "UserAdminAudits",
                columns: new[] { "UserId", "CreatedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAdminAudits");

            migrationBuilder.DropColumn(
                name: "BlockReason",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BlockedByName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BlockedByUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BlockedOn",
                table: "AspNetUsers");
        }
    }
}
