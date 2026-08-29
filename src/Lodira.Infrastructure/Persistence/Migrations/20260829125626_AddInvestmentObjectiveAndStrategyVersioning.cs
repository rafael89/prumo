using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentObjectiveAndStrategyVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioAllocationStrategies_PortfolioId",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "PortfolioAllocationStrategies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CapitalPreservationPriority",
                table: "PortfolioAllocationStrategies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ContributionPolicy",
                table: "PortfolioAllocationStrategies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GrowthPriority",
                table: "PortfolioAllocationStrategies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IncomePriority",
                table: "PortfolioAllocationStrategies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumAssetWeight",
                table: "PortfolioAllocationStrategies",
                type: "numeric(20,8)",
                precision: 20,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "PortfolioAllocationStrategies",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TemplateKey",
                table: "PortfolioAllocationStrategies",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "PortfolioAllocationStrategies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE \"PortfolioAllocationStrategies\" SET \"Version\" = 1 WHERE \"Version\" = 0");
            migrationBuilder.Sql("UPDATE \"PortfolioAllocationStrategies\" SET \"Name\" = 'Minha estratégia' WHERE \"Name\" = ''");

            migrationBuilder.CreateTable(
                name: "InvestmentObjectives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryGoal = table.Column<int>(type: "integer", nullable: false),
                    TargetMetric = table.Column<int>(type: "integer", nullable: false),
                    TargetAmount = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    TimeHorizon = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentObjectives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentObjectives_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAllocationStrategies_PortfolioId",
                table: "PortfolioAllocationStrategies",
                column: "PortfolioId",
                unique: true,
                filter: "\"ArchivedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAllocationStrategies_PortfolioId_Version",
                table: "PortfolioAllocationStrategies",
                columns: new[] { "PortfolioId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentObjectives_PortfolioId",
                table: "InvestmentObjectives",
                column: "PortfolioId",
                unique: true,
                filter: "\"ArchivedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentObjectives_PortfolioId_Version",
                table: "InvestmentObjectives",
                columns: new[] { "PortfolioId", "Version" },
                unique: true);

            // Existing installations may contain legacy orphan strategies. Do not
            // destroy or reassign those records while applying this additive R1 migration.
            // The EF model/configuration retains the relationship for clean databases.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestmentObjectives");

            migrationBuilder.DropIndex(
                name: "IX_PortfolioAllocationStrategies_PortfolioId",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.DropIndex(
                name: "IX_PortfolioAllocationStrategies_PortfolioId_Version",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.DropColumn(
                name: "CapitalPreservationPriority",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.DropColumn(
                name: "ContributionPolicy",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.DropColumn(
                name: "GrowthPriority",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.DropColumn(
                name: "IncomePriority",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.DropColumn(
                name: "MaximumAssetWeight",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.DropColumn(
                name: "TemplateKey",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PortfolioAllocationStrategies");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAllocationStrategies_PortfolioId",
                table: "PortfolioAllocationStrategies",
                column: "PortfolioId",
                unique: true);
        }
    }
}
