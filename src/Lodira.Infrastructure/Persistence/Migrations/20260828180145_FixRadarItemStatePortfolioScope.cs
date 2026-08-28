using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixRadarItemStatePortfolioScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RadarItemStates_DocumentAnalysisId",
                table: "RadarItemStates");

            migrationBuilder.AddColumn<Guid>(
                name: "PortfolioId",
                table: "RadarItemStates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE \"RadarItemStates\" AS r
                SET \"PortfolioId\" = p.\"PortfolioId\"
                FROM \"DocumentAnalyses\" AS da
                INNER JOIN \"Documents\" AS d ON d.\"Id\" = da.\"DocumentId\"
                INNER JOIN \"PortfolioPositions\" AS p ON p.\"AssetId\" = d.\"AssetId\"
                WHERE r.\"DocumentAnalysisId\" = da.\"Id\"
                  AND r.\"PortfolioId\" = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RadarItemStates_DocumentAnalysisId",
                table: "RadarItemStates",
                column: "DocumentAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_RadarItemStates_PortfolioId_DocumentAnalysisId",
                table: "RadarItemStates",
                columns: new[] { "PortfolioId", "DocumentAnalysisId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RadarItemStates_DocumentAnalysisId",
                table: "RadarItemStates");

            migrationBuilder.DropIndex(
                name: "IX_RadarItemStates_PortfolioId_DocumentAnalysisId",
                table: "RadarItemStates");

            migrationBuilder.DropColumn(
                name: "PortfolioId",
                table: "RadarItemStates");

            migrationBuilder.CreateIndex(
                name: "IX_RadarItemStates_DocumentAnalysisId",
                table: "RadarItemStates",
                column: "DocumentAnalysisId",
                unique: true);
        }
    }
}
