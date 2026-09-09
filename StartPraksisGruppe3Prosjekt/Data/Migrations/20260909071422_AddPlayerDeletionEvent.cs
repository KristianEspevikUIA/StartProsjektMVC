using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StartPraksisGruppe3Prosjekt.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerDeletionEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerDeletionEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerDeletionEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDeletionEvents_OccurredAt",
                table: "PlayerDeletionEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDeletionEvents_PlayerId",
                table: "PlayerDeletionEvents",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerDeletionEvents");
        }
    }
}
