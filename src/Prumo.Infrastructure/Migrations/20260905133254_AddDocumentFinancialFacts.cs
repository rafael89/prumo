using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentFinancialFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentFinancialFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Metric = table.Column<int>(type: "integer", nullable: false),
                    ReferencePeriod = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodType = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(24,8)", precision: 24, scale: 8, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    ExtractedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssetId1 = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFinancialFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentFinancialFacts_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentFinancialFacts_Assets_AssetId1",
                        column: x => x.AssetId1,
                        principalTable: "Assets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentFinancialFacts_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFinancialFacts_AssetId",
                table: "DocumentFinancialFacts",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFinancialFacts_AssetId1",
                table: "DocumentFinancialFacts",
                column: "AssetId1");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFinancialFacts_DocumentId_AssetId_Metric_ReferenceP~",
                table: "DocumentFinancialFacts",
                columns: new[] { "DocumentId", "AssetId", "Metric", "ReferencePeriod", "PeriodType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentFinancialFacts");
        }
    }
}
