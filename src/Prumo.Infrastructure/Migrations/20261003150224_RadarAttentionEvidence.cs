using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RadarAttentionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttentionBasis",
                table: "DocumentAnalyses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttentionEvidence",
                table: "DocumentAnalyses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttentionReason",
                table: "DocumentAnalyses",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttentionBasis",
                table: "DocumentAnalyses");

            migrationBuilder.DropColumn(
                name: "AttentionEvidence",
                table: "DocumentAnalyses");

            migrationBuilder.DropColumn(
                name: "AttentionReason",
                table: "DocumentAnalyses");
        }
    }
}
