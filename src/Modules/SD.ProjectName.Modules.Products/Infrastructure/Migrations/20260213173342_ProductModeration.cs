using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SD.ProjectName.Modules.Products.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastModeratedBy",
                table: "ProductModel",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastModeratedOn",
                table: "ProductModel",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationNote",
                table: "ProductModel",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationStatus",
                table: "ProductModel",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.Sql("UPDATE ProductModel SET ModerationStatus = 'approved';");

            migrationBuilder.CreateTable(
                name: "ProductModerationAudit",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    FromStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductModerationAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductModerationAudit_ProductModel_ProductId",
                        column: x => x.ProductId,
                        principalTable: "ProductModel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductModel_ModerationStatus",
                table: "ProductModel",
                column: "ModerationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ProductModerationAudit_ProductId",
                table: "ProductModerationAudit",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductModerationAudit");

            migrationBuilder.DropIndex(
                name: "IX_ProductModel_ModerationStatus",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "LastModeratedBy",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "LastModeratedOn",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "ModerationNote",
                table: "ProductModel");

            migrationBuilder.DropColumn(
                name: "ModerationStatus",
                table: "ProductModel");
        }
    }
}
