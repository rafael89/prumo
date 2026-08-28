using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceManualTargetsWithClassAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TargetAllocations");

            // Manual per-asset strategies are experimental data and cannot be converted
            // safely without user intent; force a fresh class-level configuration.
            migrationBuilder.Sql("DELETE FROM \"PortfolioAllocationStrategies\"");

            migrationBuilder.AddColumn<int>(
                name: "AssetClass",
                table: "Assets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Preserve the reliable classification already present in AssetType for existing data.
            migrationBuilder.Sql("UPDATE \"Assets\" SET \"AssetClass\" = CASE \"AssetType\" WHEN 0 THEN 0 WHEN 1 THEN 1 WHEN 2 THEN 4 ELSE 4 END");

            migrationBuilder.CreateTable(
                name: "StrategyAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetClass = table.Column<int>(type: "integer", nullable: false),
                    TargetPercentage = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyAllocations_PortfolioAllocationStrategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "PortfolioAllocationStrategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyAllocations_StrategyId_AssetClass",
                table: "StrategyAllocations",
                columns: new[] { "StrategyId", "AssetClass" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StrategyAllocations");

            migrationBuilder.DropColumn(
                name: "AssetClass",
                table: "Assets");

            migrationBuilder.CreateTable(
                name: "TargetAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetPercentage = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TargetAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TargetAllocations_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TargetAllocations_PortfolioAllocationStrategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "PortfolioAllocationStrategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TargetAllocations_AssetId",
                table: "TargetAllocations",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TargetAllocations_StrategyId_AssetId",
                table: "TargetAllocations",
                columns: new[] { "StrategyId", "AssetId" },
                unique: true);
        }
    }
}
