using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StartPraksisGruppe3Prosjekt.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSuccessionPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerSuccessionProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    ContractType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ContractEndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TrainingGroup = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSuccessionProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerSuccessionProfiles_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SuccessionAssessments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    RaterUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CycleStartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CatalogVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RatedAs = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AbilityCategory = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    FirstPosition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SecondPosition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ThirdPosition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PersonalReadiness = table.Column<int>(type: "integer", nullable: true),
                    Projection0To6Months = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Projection6To18Months = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Projection18To36Months = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PathwayBlocked = table.Column<bool>(type: "boolean", nullable: true),
                    WhatNow = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SuccessionRisk = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ExternalNeeded = table.Column<bool>(type: "boolean", nullable: true),
                    KeyDevelopmentFocus = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SuperStrengths = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuccessionAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuccessionAssessments_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SuccessionRatings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssessmentId = table.Column<int>(type: "integer", nullable: false),
                    RatingKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuccessionRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuccessionRatings_SuccessionAssessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "SuccessionAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSuccessionProfiles_PlayerId",
                table: "PlayerSuccessionProfiles",
                column: "PlayerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SuccessionAssessments_CycleStartsOn",
                table: "SuccessionAssessments",
                column: "CycleStartsOn");

            migrationBuilder.CreateIndex(
                name: "IX_SuccessionAssessments_PlayerId_CycleStartsOn_RaterUserId",
                table: "SuccessionAssessments",
                columns: new[] { "PlayerId", "CycleStartsOn", "RaterUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SuccessionRatings_AssessmentId_RatingKey",
                table: "SuccessionRatings",
                columns: new[] { "AssessmentId", "RatingKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerSuccessionProfiles");

            migrationBuilder.DropTable(
                name: "SuccessionRatings");

            migrationBuilder.DropTable(
                name: "SuccessionAssessments");
        }
    }
}
