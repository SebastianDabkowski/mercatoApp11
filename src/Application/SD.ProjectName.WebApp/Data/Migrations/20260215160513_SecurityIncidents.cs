using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SecurityIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsentDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConsentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    AllowPreselect = table.Column<bool>(type: "bit", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    LegalBasis = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DataCategories = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    DataSubjects = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Processors = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    RetentionPeriod = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DataTransfers = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SecurityMeasures = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityIncidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Rule = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DetectedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastStatusOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastStatusBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastStatusByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityIncidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsentVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConsentDefinitionId = table.Column<int>(type: "int", nullable: false),
                    VersionTag = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentVersions_ConsentDefinitions_ConsentDefinitionId",
                        column: x => x.ConsentDefinitionId,
                        principalTable: "ConsentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingActivityRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessingActivityId = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Updated"),
                    ChangedFields = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChangedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ChangedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingActivityRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessingActivityRevisions_ProcessingActivities_ProcessingActivityId",
                        column: x => x.ProcessingActivityId,
                        principalTable: "ProcessingActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityIncidentStatusChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ChangedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityIncidentStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityIncidentStatusChanges_SecurityIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "SecurityIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserConsentDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConsentVersionId = table.Column<int>(type: "int", nullable: false),
                    Granted = table.Column<bool>(type: "bit", nullable: false),
                    DecidedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserConsentDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserConsentDecisions_ConsentVersions_ConsentVersionId",
                        column: x => x.ConsentVersionId,
                        principalTable: "ConsentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentDefinitions_ConsentType",
                table: "ConsentDefinitions",
                column: "ConsentType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsentVersions_ConsentDefinitionId_VersionTag",
                table: "ConsentVersions",
                columns: new[] { "ConsentDefinitionId", "VersionTag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingActivities_Name",
                table: "ProcessingActivities",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingActivityRevisions_ProcessingActivityId",
                table: "ProcessingActivityRevisions",
                column: "ProcessingActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityIncidentStatusChanges_IncidentId",
                table: "SecurityIncidentStatusChanges",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserConsentDecisions_ConsentVersionId",
                table: "UserConsentDecisions",
                column: "ConsentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserConsentDecisions_UserId_ConsentVersionId",
                table: "UserConsentDecisions",
                columns: new[] { "UserId", "ConsentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserConsentDecisions_UserId_DecidedOn",
                table: "UserConsentDecisions",
                columns: new[] { "UserId", "DecidedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessingActivityRevisions");

            migrationBuilder.DropTable(
                name: "SecurityIncidentStatusChanges");

            migrationBuilder.DropTable(
                name: "UserConsentDecisions");

            migrationBuilder.DropTable(
                name: "ProcessingActivities");

            migrationBuilder.DropTable(
                name: "SecurityIncidents");

            migrationBuilder.DropTable(
                name: "ConsentVersions");

            migrationBuilder.DropTable(
                name: "ConsentDefinitions");
        }
    }
}
