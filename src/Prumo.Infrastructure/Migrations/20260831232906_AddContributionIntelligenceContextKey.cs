using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContributionIntelligenceContextKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContributionGuidances_PortfolioId_PortfolioSnapshotKey_Targ~",
                table: "ContributionGuidances");

            migrationBuilder.AddColumn<string>(
                name: "IntelligenceContextKey",
                table: "ContributionGuidances",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidances_PortfolioId_PortfolioSnapshotKey_Targ~",
                table: "ContributionGuidances",
                columns: new[] { "PortfolioId", "PortfolioSnapshotKey", "TargetAllocationKey", "IntelligenceContextKey", "RequestedAmount", "GeneratedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContributionGuidances_PortfolioId_PortfolioSnapshotKey_Targ~",
                table: "ContributionGuidances");

            migrationBuilder.DropColumn(
                name: "IntelligenceContextKey",
                table: "ContributionGuidances");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidances_PortfolioId_PortfolioSnapshotKey_Targ~",
                table: "ContributionGuidances",
                columns: new[] { "PortfolioId", "PortfolioSnapshotKey", "TargetAllocationKey", "RequestedAmount", "GeneratedAt" });
        }
    }
}
