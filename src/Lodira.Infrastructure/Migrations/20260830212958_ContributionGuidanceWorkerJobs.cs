using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContributionGuidanceWorkerJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContributionGuidanceJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContributionGuidanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidatesJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionGuidanceJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidanceJobs_PortfolioId_ContributionGuidanceId~",
                table: "ContributionGuidanceJobs",
                columns: new[] { "PortfolioId", "ContributionGuidanceId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContributionGuidanceJobs");
        }
    }
}
