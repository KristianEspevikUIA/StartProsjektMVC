using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StartPraksisGruppe3Prosjekt.Data.Migrations
{
    /// <inheritdoc />
    public partial class SuccessionWorkbookFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "PersonalReadiness",
                table: "SuccessionAssessments",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalNeededNote",
                table: "SuccessionAssessments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PathwayBlockedNote",
                table: "SuccessionAssessments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonalReadinessNote",
                table: "SuccessionAssessments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuccessionRiskNote",
                table: "SuccessionAssessments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalNeededNote",
                table: "SuccessionAssessments");

            migrationBuilder.DropColumn(
                name: "PathwayBlockedNote",
                table: "SuccessionAssessments");

            migrationBuilder.DropColumn(
                name: "PersonalReadinessNote",
                table: "SuccessionAssessments");

            migrationBuilder.DropColumn(
                name: "SuccessionRiskNote",
                table: "SuccessionAssessments");

            migrationBuilder.AlterColumn<int>(
                name: "PersonalReadiness",
                table: "SuccessionAssessments",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(3,1)",
                oldPrecision: 3,
                oldScale: 1,
                oldNullable: true);
        }
    }
}
