using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StartPraksisGruppe3Prosjekt.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogAndFeedbackRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedbackReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoundId = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    CoachUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    IsReleased = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackReleases_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FeedbackReleases_SurveyRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "SurveyRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerAccessEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    ViewedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ViewedByRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Context = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RoundId = table.Column<int>(type: "integer", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAccessEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerAccessEvents_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerAccessEvents_SurveyRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "SurveyRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackReleases_PlayerId",
                table: "FeedbackReleases",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackReleases_RoundId_PlayerId_OccurredAt",
                table: "FeedbackReleases",
                columns: new[] { "RoundId", "PlayerId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAccessEvents_PlayerId_OccurredAt",
                table: "PlayerAccessEvents",
                columns: new[] { "PlayerId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAccessEvents_RoundId",
                table: "PlayerAccessEvents",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAccessEvents_ViewedByUserId_OccurredAt",
                table: "PlayerAccessEvents",
                columns: new[] { "ViewedByUserId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedbackReleases");

            migrationBuilder.DropTable(
                name: "PlayerAccessEvents");
        }
    }
}
