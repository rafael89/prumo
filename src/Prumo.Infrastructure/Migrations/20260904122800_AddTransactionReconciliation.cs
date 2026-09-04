using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransactionReconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionReconciliations_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionReconciliationMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciliationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioTransactionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionReconciliationMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionReconciliationMembers_PortfolioTransactions_Port~",
                        column: x => x.PortfolioTransactionId,
                        principalTable: "PortfolioTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionReconciliationMembers_TransactionReconciliations~",
                        column: x => x.ReconciliationId,
                        principalTable: "TransactionReconciliations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionReconciliationMembers_PortfolioTransactionId",
                table: "TransactionReconciliationMembers",
                column: "PortfolioTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionReconciliationMembers_ReconciliationId_Portfolio~",
                table: "TransactionReconciliationMembers",
                columns: new[] { "ReconciliationId", "PortfolioTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionReconciliations_PortfolioId_Status",
                table: "TransactionReconciliations",
                columns: new[] { "PortfolioId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionReconciliationMembers");

            migrationBuilder.DropTable(
                name: "TransactionReconciliations");
        }
    }
}
