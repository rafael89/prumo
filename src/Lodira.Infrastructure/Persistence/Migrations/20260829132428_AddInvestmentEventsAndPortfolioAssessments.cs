using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentEventsAndPortfolioAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvestmentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentAnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Facts = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentEvents_DocumentAnalyses_DocumentAnalysisId",
                        column: x => x.DocumentAnalysisId,
                        principalTable: "DocumentAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentEventAssets",
                columns: table => new
                {
                    AffectedAssetsId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentEventId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentEventAssets", x => new { x.AffectedAssetsId, x.InvestmentEventId });
                    table.ForeignKey(
                        name: "FK_InvestmentEventAssets_Assets_AffectedAssetsId",
                        column: x => x.AffectedAssetsId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvestmentEventAssets_InvestmentEvents_InvestmentEventId",
                        column: x => x.InvestmentEventId,
                        principalTable: "InvestmentEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentEventEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentAnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentEventEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentEventEvidence_DocumentAnalyses_DocumentAnalysisId",
                        column: x => x.DocumentAnalysisId,
                        principalTable: "DocumentAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestmentEventEvidence_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestmentEventEvidence_InvestmentEvents_InvestmentEventId",
                        column: x => x.InvestmentEventId,
                        principalTable: "InvestmentEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttentionPriority = table.Column<int>(type: "integer", nullable: false),
                    ActionRequirement = table.Column<int>(type: "integer", nullable: false),
                    CapitalDecisionImpact = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UserState = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    WhyItMatters = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SuggestedBehavior = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    StrategyVersion = table.Column<int>(type: "integer", nullable: true),
                    ObjectiveVersion = table.Column<int>(type: "integer", nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioAssessments_InvestmentEvents_InvestmentEventId",
                        column: x => x.InvestmentEventId,
                        principalTable: "InvestmentEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PortfolioAssessments_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEventAssets_InvestmentEventId",
                table: "InvestmentEventAssets",
                column: "InvestmentEventId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEventEvidence_DocumentAnalysisId",
                table: "InvestmentEventEvidence",
                column: "DocumentAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEventEvidence_DocumentId",
                table: "InvestmentEventEvidence",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEventEvidence_InvestmentEventId",
                table: "InvestmentEventEvidence",
                column: "InvestmentEventId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEvents_DocumentAnalysisId_Fingerprint",
                table: "InvestmentEvents",
                columns: new[] { "DocumentAnalysisId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAssessments_InvestmentEventId",
                table: "PortfolioAssessments",
                column: "InvestmentEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAssessments_PortfolioId_InvestmentEventId",
                table: "PortfolioAssessments",
                columns: new[] { "PortfolioId", "InvestmentEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestmentEventAssets");

            migrationBuilder.DropTable(
                name: "InvestmentEventEvidence");

            migrationBuilder.DropTable(
                name: "PortfolioAssessments");

            migrationBuilder.DropTable(
                name: "InvestmentEvents");
        }
    }
}
