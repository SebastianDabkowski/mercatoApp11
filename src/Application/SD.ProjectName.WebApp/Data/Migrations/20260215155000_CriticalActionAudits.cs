using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class CriticalActionAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CriticalActionAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ResourceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IsSuccess = table.Column<bool>(type: "bit", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    OccurredOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriticalActionAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalActionAudits_ActionType_OccurredOn",
                table: "CriticalActionAudits",
                columns: new[] { "ActionType", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalActionAudits_OccurredOn",
                table: "CriticalActionAudits",
                column: "OccurredOn");

            migrationBuilder.CreateIndex(
                name: "IX_CriticalActionAudits_ResourceType_ResourceId_OccurredOn",
                table: "CriticalActionAudits",
                columns: new[] { "ResourceType", "ResourceId", "OccurredOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CriticalActionAudits");
        }
    }
}
