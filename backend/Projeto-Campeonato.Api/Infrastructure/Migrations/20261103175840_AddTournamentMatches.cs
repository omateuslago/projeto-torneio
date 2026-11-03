using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projeto_Campeonato.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Registrations_TournamentId_TeamId",
                table: "Registrations");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Registrations_TournamentId_TeamId",
                table: "Registrations",
                columns: new[] { "TournamentId", "TeamId" });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HomeScore = table.Column<int>(type: "integer", nullable: true),
                    AwayScore = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.CheckConstraint("CK_Matches_Result", "(\"Status\" = 'Pending' AND \"HomeScore\" IS NULL AND \"AwayScore\" IS NULL AND \"FinishedAt\" IS NULL) OR (\"Status\" = 'Finished' AND \"HomeScore\" IS NOT NULL AND \"AwayScore\" IS NOT NULL AND \"HomeScore\" >= 0 AND \"AwayScore\" >= 0 AND \"FinishedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_Matches_Teams", "\"HomeTeamId\" < \"AwayTeamId\"");
                    table.ForeignKey(
                        name: "FK_Matches_Registrations_TournamentId_AwayTeamId",
                        columns: x => new { x.TournamentId, x.AwayTeamId },
                        principalTable: "Registrations",
                        principalColumns: new[] { "TournamentId", "TeamId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Registrations_TournamentId_HomeTeamId",
                        columns: x => new { x.TournamentId, x.HomeTeamId },
                        principalTable: "Registrations",
                        principalColumns: new[] { "TournamentId", "TeamId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TournamentId_AwayTeamId",
                table: "Matches",
                columns: new[] { "TournamentId", "AwayTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TournamentId_HomeTeamId_AwayTeamId",
                table: "Matches",
                columns: new[] { "TournamentId", "HomeTeamId", "AwayTeamId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Registrations_TournamentId_TeamId",
                table: "Registrations");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_TournamentId_TeamId",
                table: "Registrations",
                columns: new[] { "TournamentId", "TeamId" },
                unique: true);
        }
    }
}
