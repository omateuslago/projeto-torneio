using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projeto_Campeonato.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MatchDurationMinutes",
                table: "Tournaments",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Tournaments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<DateTime>(
                name: "EndsAt",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlayingAreaId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefereeId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Referees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referees", x => x.Id);
                    table.UniqueConstraint("AK_Referees_TournamentId_Id", x => new { x.TournamentId, x.Id });
                    table.ForeignKey(
                        name: "FK_Referees_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Venues", x => x.Id);
                    table.UniqueConstraint("AK_Venues_TournamentId_Id", x => new { x.TournamentId, x.Id });
                    table.ForeignKey(
                        name: "FK_Venues_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayingAreas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayingAreas", x => x.Id);
                    table.UniqueConstraint("AK_PlayingAreas_TournamentId_Id", x => new { x.TournamentId, x.Id });
                    table.ForeignKey(
                        name: "FK_PlayingAreas_Venues_TournamentId_VenueId",
                        columns: x => new { x.TournamentId, x.VenueId },
                        principalTable: "Venues",
                        principalColumns: new[] { "TournamentId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Availabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlayingAreaId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefereeId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Availabilities", x => x.Id);
                    table.CheckConstraint("CK_Availability_Target", "num_nonnulls(\"TeamId\", \"PlayingAreaId\", \"RefereeId\") = 1");
                    table.CheckConstraint("CK_Availability_Time", "\"StartsAt\" < \"EndsAt\"");
                    table.ForeignKey(
                        name: "FK_Availabilities_PlayingAreas_TournamentId_PlayingAreaId",
                        columns: x => new { x.TournamentId, x.PlayingAreaId },
                        principalTable: "PlayingAreas",
                        principalColumns: new[] { "TournamentId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Availabilities_Referees_TournamentId_RefereeId",
                        columns: x => new { x.TournamentId, x.RefereeId },
                        principalTable: "Referees",
                        principalColumns: new[] { "TournamentId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Availabilities_Registrations_TournamentId_TeamId",
                        columns: x => new { x.TournamentId, x.TeamId },
                        principalTable: "Registrations",
                        principalColumns: new[] { "TournamentId", "TeamId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tournament_Duration",
                table: "Tournaments",
                sql: "\"MatchDurationMinutes\" BETWEEN 5 AND 480");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_StartsAt",
                table: "Matches",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TournamentId_PlayingAreaId",
                table: "Matches",
                columns: new[] { "TournamentId", "PlayingAreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TournamentId_RefereeId",
                table: "Matches",
                columns: new[] { "TournamentId", "RefereeId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Match_Schedule",
                table: "Matches",
                sql: "(\"StartsAt\" IS NULL AND \"EndsAt\" IS NULL AND \"PlayingAreaId\" IS NULL AND \"RefereeId\" IS NULL) OR (\"StartsAt\" IS NOT NULL AND \"EndsAt\" IS NOT NULL AND \"PlayingAreaId\" IS NOT NULL AND \"StartsAt\" < \"EndsAt\")");

            migrationBuilder.CreateIndex(
                name: "IX_Availabilities_TournamentId_PlayingAreaId_StartsAt_EndsAt",
                table: "Availabilities",
                columns: new[] { "TournamentId", "PlayingAreaId", "StartsAt", "EndsAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Availabilities_TournamentId_RefereeId_StartsAt_EndsAt",
                table: "Availabilities",
                columns: new[] { "TournamentId", "RefereeId", "StartsAt", "EndsAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Availabilities_TournamentId_TeamId_StartsAt_EndsAt",
                table: "Availabilities",
                columns: new[] { "TournamentId", "TeamId", "StartsAt", "EndsAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayingAreas_TournamentId_VenueId",
                table: "PlayingAreas",
                columns: new[] { "TournamentId", "VenueId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_PlayingAreas_TournamentId_PlayingAreaId",
                table: "Matches",
                columns: new[] { "TournamentId", "PlayingAreaId" },
                principalTable: "PlayingAreas",
                principalColumns: new[] { "TournamentId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_Referees_TournamentId_RefereeId",
                table: "Matches",
                columns: new[] { "TournamentId", "RefereeId" },
                principalTable: "Referees",
                principalColumns: new[] { "TournamentId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Matches_PlayingAreas_TournamentId_PlayingAreaId",
                table: "Matches");

            migrationBuilder.DropForeignKey(
                name: "FK_Matches_Referees_TournamentId_RefereeId",
                table: "Matches");

            migrationBuilder.DropTable(
                name: "Availabilities");

            migrationBuilder.DropTable(
                name: "PlayingAreas");

            migrationBuilder.DropTable(
                name: "Referees");

            migrationBuilder.DropTable(
                name: "Venues");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tournament_Duration",
                table: "Tournaments");

            migrationBuilder.DropIndex(
                name: "IX_Matches_StartsAt",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_TournamentId_PlayingAreaId",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_TournamentId_RefereeId",
                table: "Matches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Match_Schedule",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "MatchDurationMinutes",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "EndsAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PlayingAreaId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "RefereeId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "Matches");
        }
    }
}
