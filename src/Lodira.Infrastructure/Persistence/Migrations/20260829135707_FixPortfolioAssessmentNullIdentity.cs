using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixPortfolioAssessmentNullIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Strategy~",
                table: "PortfolioAssessments");

            migrationBuilder.AddColumn<int>(
                name: "ObjectiveVersionKey",
                table: "PortfolioAssessments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StrategyVersionKey",
                table: "PortfolioAssessments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Strategy~",
                table: "PortfolioAssessments",
                columns: new[] { "PortfolioId", "InvestmentEventId", "StrategyVersionKey", "ObjectiveVersionKey", "AssessmentVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Strategy~",
                table: "PortfolioAssessments");

            migrationBuilder.DropColumn(
                name: "ObjectiveVersionKey",
                table: "PortfolioAssessments");

            migrationBuilder.DropColumn(
                name: "StrategyVersionKey",
                table: "PortfolioAssessments");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Strategy~",
                table: "PortfolioAssessments",
                columns: new[] { "PortfolioId", "InvestmentEventId", "StrategyVersion", "ObjectiveVersion", "AssessmentVersion" },
                unique: true);
        }
    }
}
