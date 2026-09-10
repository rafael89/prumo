using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations;

public partial class CandidatesJsonUseText : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "CandidatesJson",
            table: "ContributionGuidanceJobs",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(200000)",
            oldMaxLength: 200000);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "CandidatesJson",
            table: "ContributionGuidanceJobs",
            type: "character varying(200000)",
            maxLength: 200000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");
    }
}
