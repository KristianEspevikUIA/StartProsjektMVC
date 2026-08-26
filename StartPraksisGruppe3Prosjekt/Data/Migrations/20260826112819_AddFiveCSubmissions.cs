using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StartPraksisGruppe3Prosjekt.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFiveCSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiveCSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoundId = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    PlayerCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RespondentRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RespondentUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    QuestionSetVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiveCSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiveCSubmissions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FiveCSubmissions_SurveyRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "SurveyRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FiveCAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubmissionId = table.Column<int>(type: "integer", nullable: false),
                    QuestionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CategoryKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiveCAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiveCAnswers_FiveCSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "FiveCSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiveCAnswers_SubmissionId_QuestionKey",
                table: "FiveCAnswers",
                columns: new[] { "SubmissionId", "QuestionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiveCSubmissions_PlayerId",
                table: "FiveCSubmissions",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_FiveCSubmissions_RoundId_PlayerId_RespondentUserId",
                table: "FiveCSubmissions",
                columns: new[] { "RoundId", "PlayerId", "RespondentUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiveCAnswers");

            migrationBuilder.DropTable(
                name: "FiveCSubmissions");
        }
    }
}
