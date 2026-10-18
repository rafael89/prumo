using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentContentDeduplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateOfDocumentId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextHash",
                table: "Documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_AssetId_TextHash",
                table: "Documents",
                columns: new[] { "AssetId", "TextHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DuplicateOfDocumentId",
                table: "Documents",
                column: "DuplicateOfDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Documents_DuplicateOfDocumentId",
                table: "Documents",
                column: "DuplicateOfDocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Documents_DuplicateOfDocumentId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_AssetId_TextHash",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_DuplicateOfDocumentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DuplicateOfDocumentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TextHash",
                table: "Documents");
        }
    }
}
