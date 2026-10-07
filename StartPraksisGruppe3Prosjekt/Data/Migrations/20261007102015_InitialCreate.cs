using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StartPraksisGruppe3Prosjekt.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asp_net_roles",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_users",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "database_marker",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_database_marker", x => x.id);
                    table.CheckConstraint("ck_database_marker_environment", "environment in ('Development', 'Production')");
                    table.CheckConstraint("ck_database_marker_single_row", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "player_deletion_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<int>(type: "integer", nullable: false),
                    deleted_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_deletion_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "survey_rounds",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_rounds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<string>(type: "text", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_role_claims_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "asp_net_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_user_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_asp_net_user_logins_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_roles",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "asp_net_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_tokens",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_asp_net_user_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "players",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    team_id = table.Column<int>(type: "integer", nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    position = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_players", x => x.id);
                    table.ForeignKey(
                        name: "fk_players_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_players_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consent_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    changed_by_user_id = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_consent_events_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feedback_releases",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    round_id = table.Column<int>(type: "integer", nullable: false),
                    player_id = table.Column<int>(type: "integer", nullable: false),
                    coach_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    is_released = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_releases", x => x.id);
                    table.ForeignKey(
                        name: "fk_feedback_releases_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feedback_releases_survey_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "survey_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "five_c_submissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    round_id = table.Column<int>(type: "integer", nullable: false),
                    player_id = table.Column<int>(type: "integer", nullable: false),
                    respondent_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    respondent_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    question_set_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_five_c_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_five_c_submissions_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_five_c_submissions_survey_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "survey_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guardianships",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<int>(type: "integer", nullable: false),
                    guardian_user_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardianships", x => x.id);
                    table.ForeignKey(
                        name: "fk_guardianships_asp_net_users_guardian_user_id",
                        column: x => x.guardian_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guardianships_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_access_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<int>(type: "integer", nullable: false),
                    viewed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    viewed_by_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    context = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    round_id = table.Column<int>(type: "integer", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_access_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_player_access_events_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_player_access_events_survey_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "survey_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "player_personal_details",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<int>(type: "integer", nullable: false),
                    first_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    photo = table.Column<byte[]>(type: "bytea", nullable: true),
                    photo_content_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    photo_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    photo_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_personal_details", x => x.id);
                    table.ForeignKey(
                        name: "fk_player_personal_details_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_succession_profiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<int>(type: "integer", nullable: false),
                    contract_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    contract_ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    training_group = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    updated_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_succession_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_player_succession_profiles_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "succession_assessments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<int>(type: "integer", nullable: false),
                    rater_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    cycle_starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    catalog_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rated_as = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ability_category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    first_position = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    second_position = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    third_position = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    personal_readiness = table.Column<int>(type: "integer", nullable: true),
                    projection0to6months = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    projection6to18months = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    projection18to36months = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pathway_blocked = table.Column<bool>(type: "boolean", nullable: true),
                    what_now = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    succession_risk = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    external_needed = table.Column<bool>(type: "boolean", nullable: true),
                    key_development_focus = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    super_strengths = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_succession_assessments", x => x.id);
                    table.ForeignKey(
                        name: "fk_succession_assessments_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "five_c_answers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    submission_id = table.Column<int>(type: "integer", nullable: false),
                    question_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_five_c_answers", x => x.id);
                    table.ForeignKey(
                        name: "fk_five_c_answers_five_c_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "five_c_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "five_c_reflection_answers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    submission_id = table.Column<int>(type: "integer", nullable: false),
                    question_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_five_c_reflection_answers", x => x.id);
                    table.ForeignKey(
                        name: "fk_five_c_reflection_answers_five_c_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "five_c_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "succession_ratings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    assessment_id = table.Column<int>(type: "integer", nullable: false),
                    rating_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_succession_ratings", x => x.id);
                    table.ForeignKey(
                        name: "fk_succession_ratings_succession_assessments_assessment_id",
                        column: x => x.assessment_id,
                        principalTable: "succession_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_role_claims_role_id",
                table: "asp_net_role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_roles_normalized_name",
                table: "asp_net_roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_claims_user_id",
                table: "asp_net_user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_logins_user_id",
                table: "asp_net_user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_roles_role_id",
                table: "asp_net_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_normalized_email",
                table: "asp_net_users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_normalized_user_name",
                table: "asp_net_users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consent_events_player_id_occurred_at",
                table: "consent_events",
                columns: new[] { "player_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_releases_player_id",
                table: "feedback_releases",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_releases_round_id_player_id_occurred_at",
                table: "feedback_releases",
                columns: new[] { "round_id", "player_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_five_c_answers_submission_id_question_key",
                table: "five_c_answers",
                columns: new[] { "submission_id", "question_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_five_c_reflection_answers_submission_id_question_key",
                table: "five_c_reflection_answers",
                columns: new[] { "submission_id", "question_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_five_c_submissions_player_id",
                table: "five_c_submissions",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_five_c_submissions_round_id_player_id_respondent_user_id",
                table: "five_c_submissions",
                columns: new[] { "round_id", "player_id", "respondent_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guardianships_guardian_user_id",
                table: "guardianships",
                column: "guardian_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_guardianships_player_id_guardian_user_id",
                table: "guardianships",
                columns: new[] { "player_id", "guardian_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_access_events_player_id_occurred_at",
                table: "player_access_events",
                columns: new[] { "player_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_player_access_events_round_id",
                table: "player_access_events",
                column: "round_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_access_events_viewed_by_user_id_occurred_at",
                table: "player_access_events",
                columns: new[] { "viewed_by_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_player_deletion_events_occurred_at",
                table: "player_deletion_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_player_deletion_events_player_id",
                table: "player_deletion_events",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_personal_details_player_id",
                table: "player_personal_details",
                column: "player_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_succession_profiles_player_id",
                table: "player_succession_profiles",
                column: "player_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_players_name",
                table: "players",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_players_team_id",
                table: "players",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_players_user_id",
                table: "players",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_succession_assessments_cycle_starts_on",
                table: "succession_assessments",
                column: "cycle_starts_on");

            migrationBuilder.CreateIndex(
                name: "ix_succession_assessments_player_id_cycle_starts_on_rater_user",
                table: "succession_assessments",
                columns: new[] { "player_id", "cycle_starts_on", "rater_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_succession_ratings_assessment_id_rating_key",
                table: "succession_ratings",
                columns: new[] { "assessment_id", "rating_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_name",
                table: "teams",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asp_net_role_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_logins");

            migrationBuilder.DropTable(
                name: "asp_net_user_roles");

            migrationBuilder.DropTable(
                name: "asp_net_user_tokens");

            migrationBuilder.DropTable(
                name: "consent_events");

            migrationBuilder.DropTable(
                name: "database_marker");

            migrationBuilder.DropTable(
                name: "feedback_releases");

            migrationBuilder.DropTable(
                name: "five_c_answers");

            migrationBuilder.DropTable(
                name: "five_c_reflection_answers");

            migrationBuilder.DropTable(
                name: "guardianships");

            migrationBuilder.DropTable(
                name: "player_access_events");

            migrationBuilder.DropTable(
                name: "player_deletion_events");

            migrationBuilder.DropTable(
                name: "player_personal_details");

            migrationBuilder.DropTable(
                name: "player_succession_profiles");

            migrationBuilder.DropTable(
                name: "succession_ratings");

            migrationBuilder.DropTable(
                name: "asp_net_roles");

            migrationBuilder.DropTable(
                name: "five_c_submissions");

            migrationBuilder.DropTable(
                name: "succession_assessments");

            migrationBuilder.DropTable(
                name: "survey_rounds");

            migrationBuilder.DropTable(
                name: "players");

            migrationBuilder.DropTable(
                name: "asp_net_users");

            migrationBuilder.DropTable(
                name: "teams");
        }
    }
}
