using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PartialContributionExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioTransactions_ContributionGuidanceId",
                table: "PortfolioTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactions_ContributionGuidanceId",
                table: "PortfolioTransactions",
                column: "ContributionGuidanceId",
                filter: "\"ContributionGuidanceId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioTransactions_ContributionGuidanceId",
                table: "PortfolioTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactions_ContributionGuidanceId",
                table: "PortfolioTransactions",
                column: "ContributionGuidanceId",
                unique: true,
                filter: "\"ContributionGuidanceId\" IS NOT NULL");
        }
    }
}
