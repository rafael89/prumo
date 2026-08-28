using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessingJobs_DocumentId_JobType",
                table: "ProcessingJobs");

            migrationBuilder.CreateTable(
                name: "DocumentAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessingJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Importance = table.Column<int>(type: "integer", nullable: false),
                    Classification = table.Column<int>(type: "integer", nullable: false),
                    RequiresAction = table.Column<bool>(type: "boolean", nullable: false),
                    SuggestedAction = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Impact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    PortfolioImpact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AffectedAssets = table.Column<List<string>>(type: "jsonb", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    RawResponse = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentAnalyses_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAnalyses_DocumentId_CreatedAt",
                table: "DocumentAnalyses",
                columns: new[] { "DocumentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAnalyses_ProcessingJobId",
                table: "DocumentAnalyses",
                column: "ProcessingJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_DocumentId_JobType",
                table: "ProcessingJobs",
                columns: new[] { "DocumentId", "JobType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentAnalyses");

            migrationBuilder.DropIndex(
                name: "IX_ProcessingJobs_DocumentId_JobType",
                table: "ProcessingJobs");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_DocumentId_JobType",
                table: "ProcessingJobs",
                columns: new[] { "DocumentId", "JobType" },
                unique: true);
        }
    }
}
