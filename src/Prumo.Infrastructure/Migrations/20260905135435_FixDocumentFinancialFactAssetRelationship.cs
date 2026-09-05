using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixDocumentFinancialFactAssetRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentFinancialFacts_Assets_AssetId1",
                table: "DocumentFinancialFacts");

            migrationBuilder.DropIndex(
                name: "IX_DocumentFinancialFacts_AssetId1",
                table: "DocumentFinancialFacts");

            migrationBuilder.DropColumn(
                name: "AssetId1",
                table: "DocumentFinancialFacts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssetId1",
                table: "DocumentFinancialFacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFinancialFacts_AssetId1",
                table: "DocumentFinancialFacts",
                column: "AssetId1");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentFinancialFacts_Assets_AssetId1",
                table: "DocumentFinancialFacts",
                column: "AssetId1",
                principalTable: "Assets",
                principalColumn: "Id");
        }
    }
}
