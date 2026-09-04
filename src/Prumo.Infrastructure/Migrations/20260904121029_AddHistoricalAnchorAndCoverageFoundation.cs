using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricalAnchorAndCoverageFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialHistoryCoverages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialHistoryCoverages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialHistoryCoverages_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialHistoryCoverages_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioHistoricalAnchors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnchorDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    DatePrecision = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioHistoricalAnchors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioHistoricalAnchors_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioHistoricalAnchorItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnchorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    AverageCost = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioHistoricalAnchorItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioHistoricalAnchorItems_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortfolioHistoricalAnchorItems_PortfolioHistoricalAnchors_A~",
                        column: x => x.AnchorId,
                        principalTable: "PortfolioHistoricalAnchors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialHistoryCoverages_AssetId",
                table: "FinancialHistoryCoverages",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialHistoryCoverages_PortfolioId_AssetId_Dimension",
                table: "FinancialHistoryCoverages",
                columns: new[] { "PortfolioId", "AssetId", "Dimension" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialHistoryCoverages_PortfolioId_Dimension_FromDate_To~",
                table: "FinancialHistoryCoverages",
                columns: new[] { "PortfolioId", "Dimension", "FromDate", "ToDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoricalAnchorItems_AnchorId_AssetId",
                table: "PortfolioHistoricalAnchorItems",
                columns: new[] { "AnchorId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoricalAnchorItems_AssetId",
                table: "PortfolioHistoricalAnchorItems",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoricalAnchors_PortfolioId_AnchorDate",
                table: "PortfolioHistoricalAnchors",
                columns: new[] { "PortfolioId", "AnchorDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialHistoryCoverages");

            migrationBuilder.DropTable(
                name: "PortfolioHistoricalAnchorItems");

            migrationBuilder.DropTable(
                name: "PortfolioHistoricalAnchors");
        }
    }
}
