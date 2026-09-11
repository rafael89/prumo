using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations;

public partial class CvmFiiMonthlyStructuredIngestion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CvmFiiMonthlyObservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                FundClassCnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                ReferenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                SourceVersion = table.Column<int>(type: "integer", nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                SchemaVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                NetAssetValue = table.Column<decimal>(type: "numeric(24,8)", precision: 24, scale: 8, nullable: true),
                NavPerShare = table.Column<decimal>(type: "numeric(24,8)", precision: 24, scale: 8, nullable: true),
                Cash = table.Column<decimal>(type: "numeric(24,8)", precision: 24, scale: 8, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CvmFiiMonthlyObservations", x => x.Id);
                table.ForeignKey("FK_CvmFiiMonthlyObservations_Assets_AssetId", x => x.AssetId, "Assets", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CvmFiiMonthlyObservations_AssetId_FundClassCnpj_ReferenceDa~",
            table: "CvmFiiMonthlyObservations",
            columns: new[] { "AssetId", "FundClassCnpj", "ReferenceDate", "ObservedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_CvmFiiMonthlyObservations_Fingerprint",
            table: "CvmFiiMonthlyObservations",
            column: "Fingerprint",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "CvmFiiMonthlyObservations");
}
