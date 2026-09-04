using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetFinancialSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetFundamentalSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodType = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    NetIncome = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Equity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    TotalDebt = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Cash = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Ebitda = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Roe = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Roic = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    NetMargin = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetFundamentalSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetFundamentalSnapshots_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetValuationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PriceEarnings = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    PriceBook = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    EvEbitda = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    DividendYield = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetValuationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetValuationSnapshots_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetFundamentalSnapshots_AssetId_Source_ReferenceDate_Peri~",
                table: "AssetFundamentalSnapshots",
                columns: new[] { "AssetId", "Source", "ReferenceDate", "PeriodType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetValuationSnapshots_AssetId_Source_ReferenceDate",
                table: "AssetValuationSnapshots",
                columns: new[] { "AssetId", "Source", "ReferenceDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetFundamentalSnapshots");

            migrationBuilder.DropTable(
                name: "AssetValuationSnapshots");
        }
    }
}
