using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OccurredOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    SellerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    Keyword = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventType",
                table: "AnalyticsEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventType_OccurredOn",
                table: "AnalyticsEvents",
                columns: new[] { "EventType", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_OccurredOn",
                table: "AnalyticsEvents",
                column: "OccurredOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsEvents");
        }
    }
}
