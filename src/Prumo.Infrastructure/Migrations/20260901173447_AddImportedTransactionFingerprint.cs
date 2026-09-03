using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportedTransactionFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportFingerprint",
                table: "PortfolioTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactions_PortfolioId_ImportFingerprint",
                table: "PortfolioTransactions",
                columns: new[] { "PortfolioId", "ImportFingerprint" },
                unique: true,
                filter: "\"ImportFingerprint\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioTransactions_PortfolioId_ImportFingerprint",
                table: "PortfolioTransactions");

            migrationBuilder.DropColumn(
                name: "ImportFingerprint",
                table: "PortfolioTransactions");
        }
    }
}
