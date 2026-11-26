using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PortfolioPositionQuantities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioPositionQuantities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioPositionQuantities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioPositionQuantities_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PortfolioPositionQuantities_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioPositionQuantities_AssetId",
                table: "PortfolioPositionQuantities",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioPositionQuantities_PortfolioId_AssetId_RecordedAt",
                table: "PortfolioPositionQuantities",
                columns: new[] { "PortfolioId", "AssetId", "RecordedAt" });

            migrationBuilder.Sql("""
                INSERT INTO "PortfolioPositionQuantities" ("Id", "PortfolioId", "AssetId", "RecordedAt", "Quantity")
                SELECT "Id", "PortfolioId", "AssetId", CURRENT_TIMESTAMP, "Quantity"
                FROM "PortfolioPositions"
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioPositionQuantities");
        }
    }
}
