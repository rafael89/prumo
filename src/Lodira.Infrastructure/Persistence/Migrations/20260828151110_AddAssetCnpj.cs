using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodira.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetCnpj : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cnpj",
                table: "Assets",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Cnpj",
                table: "Assets",
                column: "Cnpj");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_Cnpj",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Cnpj",
                table: "Assets");
        }
    }
}
