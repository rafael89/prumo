using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PrepareMultiUserFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DocumentAnalyses_DocumentId_Model_PromptVersion",
                table: "DocumentAnalyses",
                columns: new[] { "DocumentId", "Model", "PromptVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentAnalyses_DocumentId_Model_PromptVersion",
                table: "DocumentAnalyses");
        }
    }
}
