using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceActiveDocumentJobUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessingJobs_DocumentId_JobType",
                table: "ProcessingJobs");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_DocumentId_JobType",
                table: "ProcessingJobs",
                columns: new[] { "DocumentId", "JobType" },
                unique: true,
                filter: "\"Status\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessingJobs_DocumentId_JobType",
                table: "ProcessingJobs");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_DocumentId_JobType",
                table: "ProcessingJobs",
                columns: new[] { "DocumentId", "JobType" });
        }
    }
}
