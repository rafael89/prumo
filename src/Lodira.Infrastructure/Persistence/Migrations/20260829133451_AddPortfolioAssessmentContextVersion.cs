using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioAssessmentContextVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioAssessments_PortfolioId_InvestmentEventId",
                table: "PortfolioAssessments");

            migrationBuilder.AddColumn<string>(
                name: "AssessmentVersion",
                table: "PortfolioAssessments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Strategy~",
                table: "PortfolioAssessments",
                columns: new[] { "PortfolioId", "InvestmentEventId", "StrategyVersion", "ObjectiveVersion", "AssessmentVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Strategy~",
                table: "PortfolioAssessments");

            migrationBuilder.DropColumn(
                name: "AssessmentVersion",
                table: "PortfolioAssessments");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAssessments_PortfolioId_InvestmentEventId",
                table: "PortfolioAssessments",
                columns: new[] { "PortfolioId", "InvestmentEventId" },
                unique: true);
        }
    }
}
