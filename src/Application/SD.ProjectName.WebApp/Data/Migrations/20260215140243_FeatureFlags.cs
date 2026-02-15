using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class FeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureFlags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DefaultEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureFlagAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlagId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Updated"),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ChangedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlagAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureFlagAudits_FeatureFlags_FlagId",
                        column: x => x.FlagId,
                        principalTable: "FeatureFlags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeatureFlagEnvironments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlagId = table.Column<int>(type: "int", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    TargetingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlagEnvironments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureFlagEnvironments_FeatureFlags_FlagId",
                        column: x => x.FlagId,
                        principalTable: "FeatureFlags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagAudits_FlagId",
                table: "FeatureFlagAudits",
                column: "FlagId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagEnvironments_FlagId_Environment",
                table: "FeatureFlagEnvironments",
                columns: new[] { "FlagId", "Environment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_Key",
                table: "FeatureFlags",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureFlagAudits");

            migrationBuilder.DropTable(
                name: "FeatureFlagEnvironments");

            migrationBuilder.DropTable(
                name: "FeatureFlags");
        }
    }
}
