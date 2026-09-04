using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetExposureMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EconomicCurrencyExposureCoverage",
                table: "Assets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GeographicExposureCoverage",
                table: "Assets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ListingCountryCode",
                table: "Assets",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SectorCategory",
                table: "Assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradingCurrency",
                table: "Assets",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetCurrencyExposures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDerived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCurrencyExposures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetCurrencyExposures_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetGeographicExposures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDerived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetGeographicExposures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetGeographicExposures_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetCurrencyExposures_AssetId_CurrencyCode",
                table: "AssetCurrencyExposures",
                columns: new[] { "AssetId", "CurrencyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetGeographicExposures_AssetId_CountryCode",
                table: "AssetGeographicExposures",
                columns: new[] { "AssetId", "CountryCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetCurrencyExposures");

            migrationBuilder.DropTable(
                name: "AssetGeographicExposures");

            migrationBuilder.DropColumn(
                name: "EconomicCurrencyExposureCoverage",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "GeographicExposureCoverage",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ListingCountryCode",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "SectorCategory",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "TradingCurrency",
                table: "Assets");
        }
    }
}
