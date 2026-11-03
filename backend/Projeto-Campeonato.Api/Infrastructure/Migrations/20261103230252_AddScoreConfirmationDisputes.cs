using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projeto_Campeonato.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScoreConfirmationDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Matches_Result",
                table: "Matches");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Matches",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<string>(
                name: "ResultSource",
                table: "Matches",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Disputes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disputes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Disputes_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScoreSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedByTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeScore = table.Column<int>(type: "integer", nullable: false),
                    AwayScore = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreSubmissions", x => x.Id);
                    table.CheckConstraint("CK_Submission_Scores", "\"HomeScore\" >= 0 AND \"AwayScore\" >= 0");
                    table.ForeignKey(
                        name: "FK_ScoreSubmissions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoreSubmissions_Teams_SubmittedByTeamId",
                        column: x => x.SubmittedByTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Matches_Result",
                table: "Matches",
                sql: "(\"Status\" IN ('Pending', 'AwaitingConfirmation', 'Disputed') AND \"HomeScore\" IS NULL AND \"AwayScore\" IS NULL AND \"FinishedAt\" IS NULL) OR (\"Status\" = 'Finished' AND \"HomeScore\" IS NOT NULL AND \"AwayScore\" IS NOT NULL AND \"HomeScore\" >= 0 AND \"AwayScore\" >= 0 AND \"FinishedAt\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_MatchId_OccurredAt",
                table: "AuditLogs",
                columns: new[] { "MatchId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TeamId",
                table: "AuditLogs",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_MatchId",
                table: "Disputes",
                column: "MatchId",
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_ScoreSubmissions_MatchId_SubmittedByTeamId",
                table: "ScoreSubmissions",
                columns: new[] { "MatchId", "SubmittedByTeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScoreSubmissions_SubmittedByTeamId",
                table: "ScoreSubmissions",
                column: "SubmittedByTeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Disputes");

            migrationBuilder.DropTable(
                name: "ScoreSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Matches_Result",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ResultSource",
                table: "Matches");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Matches",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Matches_Result",
                table: "Matches",
                sql: "(\"Status\" = 'Pending' AND \"HomeScore\" IS NULL AND \"AwayScore\" IS NULL AND \"FinishedAt\" IS NULL) OR (\"Status\" = 'Finished' AND \"HomeScore\" IS NOT NULL AND \"AwayScore\" IS NOT NULL AND \"HomeScore\" >= 0 AND \"AwayScore\" >= 0 AND \"FinishedAt\" IS NOT NULL)");
        }
    }
}
