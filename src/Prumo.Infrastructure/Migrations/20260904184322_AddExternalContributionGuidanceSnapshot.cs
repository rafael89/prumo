using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalContributionGuidanceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContributionGuidanceAllocations_ContributionGuidanceId_Asse~",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.AlterColumn<Guid>(
                name: "AssetId",
                table: "ContributionGuidanceAllocations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "AssetClass",
                table: "ContributionGuidanceAllocations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssetType",
                table: "ContributionGuidanceAllocations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CandidateKey",
                table: "ContributionGuidanceAllocations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "ContributionGuidanceAllocations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Exchange",
                table: "ContributionGuidanceAllocations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ContributionGuidanceAllocations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "ContributionGuidanceAllocations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE \"ContributionGuidanceAllocations\" SET \"Origin\" = 'KnownCatalog' WHERE \"Origin\" = ''");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "ContributionGuidanceAllocations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sector",
                table: "ContributionGuidanceAllocations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "ContributionGuidanceAllocations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ticker",
                table: "ContributionGuidanceAllocations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidanceAllocations_ContributionGuidanceId_Asse~",
                table: "ContributionGuidanceAllocations",
                columns: new[] { "ContributionGuidanceId", "AssetId" },
                unique: true,
                filter: "\"AssetId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidanceAllocations_ContributionGuidanceId_Cand~",
                table: "ContributionGuidanceAllocations",
                columns: new[] { "ContributionGuidanceId", "CandidateKey" },
                unique: true,
                filter: "\"CandidateKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContributionGuidanceAllocations_ContributionGuidanceId_Asse~",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropIndex(
                name: "IX_ContributionGuidanceAllocations_ContributionGuidanceId_Cand~",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "AssetClass",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "AssetType",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "CandidateKey",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "Exchange",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "Sector",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.DropColumn(
                name: "Ticker",
                table: "ContributionGuidanceAllocations");

            migrationBuilder.AlterColumn<Guid>(
                name: "AssetId",
                table: "ContributionGuidanceAllocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidanceAllocations_ContributionGuidanceId_Asse~",
                table: "ContributionGuidanceAllocations",
                columns: new[] { "ContributionGuidanceId", "AssetId" },
                unique: true);
        }
    }
}
