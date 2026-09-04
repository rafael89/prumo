using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCorporateActionsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetCorporateActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DatePrecision = table.Column<int>(type: "integer", nullable: false),
                    FactorNumerator = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    FactorDenominator = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCorporateActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetCorporateActions_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetCorporateActions_AssetId_EffectiveDate",
                table: "AssetCorporateActions",
                columns: new[] { "AssetId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetCorporateActions_AssetId_Type_EffectiveDate_FactorNume~",
                table: "AssetCorporateActions",
                columns: new[] { "AssetId", "Type", "EffectiveDate", "FactorNumerator", "FactorDenominator", "Source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetCorporateActions");
        }
    }
}
