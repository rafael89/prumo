using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioAssessmentFreshness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"PortfolioAssessments\" ADD COLUMN IF NOT EXISTS \"InvestmentEventVersion\" integer NULL;");
            migrationBuilder.Sql("ALTER TABLE \"PortfolioAssessments\" ADD COLUMN IF NOT EXISTS \"PortfolioSnapshotKey\" character varying(64) NULL;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Strategy~\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Portfoli~\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Portfoli~\" ON \"PortfolioAssessments\" (\"PortfolioId\", \"InvestmentEventId\", \"PortfolioSnapshotKey\", \"StrategyVersionKey\", \"ObjectiveVersionKey\", \"AssessmentVersion\") WHERE \"PortfolioSnapshotKey\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Portfoli~\";");
            migrationBuilder.Sql("ALTER TABLE \"PortfolioAssessments\" DROP COLUMN IF EXISTS \"InvestmentEventVersion\";");
            migrationBuilder.Sql("ALTER TABLE \"PortfolioAssessments\" DROP COLUMN IF EXISTS \"PortfolioSnapshotKey\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Strategy~\" ON \"PortfolioAssessments\" (\"PortfolioId\", \"InvestmentEventId\", \"StrategyVersionKey\", \"ObjectiveVersionKey\", \"AssessmentVersion\");");
        }
    }
}
