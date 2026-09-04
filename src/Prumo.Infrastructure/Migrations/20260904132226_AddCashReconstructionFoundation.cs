using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashReconstructionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioCashAnchors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnchorDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    DatePrecision = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioCashAnchors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioCashAnchors_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioCashAnchors_PortfolioId_AnchorDate",
                table: "PortfolioCashAnchors",
                columns: new[] { "PortfolioId", "AnchorDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioCashAnchors");
        }
    }
}
