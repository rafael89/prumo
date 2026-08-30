using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class R4ContributionGuidanceAndTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContributionGuidances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    PortfolioSnapshotKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetAllocationKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EngineVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IntelligenceAvailability = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reasoning = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    MetadataJson = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionGuidances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContributionGuidanceAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContributionGuidanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    EvidenceJson = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionGuidanceAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContributionGuidanceAllocations_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionGuidanceAllocations_ContributionGuidances_Contr~",
                        column: x => x.ContributionGuidanceId,
                        principalTable: "ContributionGuidances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ContributionGuidanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioTransactions_ContributionGuidances_ContributionGui~",
                        column: x => x.ContributionGuidanceId,
                        principalTable: "ContributionGuidances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PortfolioTransactions_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioTransactionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioTransactionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioTransactionItems_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortfolioTransactionItems_PortfolioTransactions_PortfolioTr~",
                        column: x => x.PortfolioTransactionId,
                        principalTable: "PortfolioTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidanceAllocations_AssetId",
                table: "ContributionGuidanceAllocations",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidanceAllocations_ContributionGuidanceId_Asse~",
                table: "ContributionGuidanceAllocations",
                columns: new[] { "ContributionGuidanceId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidances_PortfolioId_PortfolioSnapshotKey_Targ~",
                table: "ContributionGuidances",
                columns: new[] { "PortfolioId", "PortfolioSnapshotKey", "TargetAllocationKey", "RequestedAmount", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactionItems_AssetId",
                table: "PortfolioTransactionItems",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactionItems_PortfolioTransactionId",
                table: "PortfolioTransactionItems",
                column: "PortfolioTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactions_ContributionGuidanceId",
                table: "PortfolioTransactions",
                column: "ContributionGuidanceId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactions_PortfolioId_OccurredAt",
                table: "PortfolioTransactions",
                columns: new[] { "PortfolioId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContributionGuidanceAllocations");

            migrationBuilder.DropTable(
                name: "PortfolioTransactionItems");

            migrationBuilder.DropTable(
                name: "PortfolioTransactions");

            migrationBuilder.DropTable(
                name: "ContributionGuidances");
        }
    }
}
