using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelaxDocumentDedupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_SourceIdentifier",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_SourceUrl",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SourceName_SourceIdentifier",
                table: "Documents",
                columns: new[] { "SourceName", "SourceIdentifier" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SourceName_SourceUrl",
                table: "Documents",
                columns: new[] { "SourceName", "SourceUrl" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_SourceName_SourceIdentifier",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_SourceName_SourceUrl",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SourceIdentifier",
                table: "Documents",
                column: "SourceIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SourceUrl",
                table: "Documents",
                column: "SourceUrl",
                unique: true);
        }
    }
}
