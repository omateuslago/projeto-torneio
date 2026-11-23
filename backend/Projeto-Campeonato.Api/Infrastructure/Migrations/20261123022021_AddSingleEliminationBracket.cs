using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projeto_Campeonato.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSingleEliminationBracket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BracketNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TopTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    BottomTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    NextNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    NextSlot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IsBye = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BracketNodes", x => x.Id);
                    table.CheckConstraint("CK_BracketNodes_Next", "(\"NextNodeId\" IS NULL) = (\"NextSlot\" IS NULL)");
                    table.CheckConstraint("CK_BracketNodes_Position", "\"RoundNumber\" > 0 AND \"Position\" > 0");
                    table.CheckConstraint("CK_BracketNodes_Teams", "\"TopTeamId\" IS NULL OR \"BottomTeamId\" IS NULL OR \"TopTeamId\" <> \"BottomTeamId\"");
                    table.ForeignKey(
                        name: "FK_BracketNodes_BracketNodes_NextNodeId",
                        column: x => x.NextNodeId,
                        principalTable: "BracketNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BracketNodes_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BracketNodes_Teams_BottomTeamId",
                        column: x => x.BottomTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BracketNodes_Teams_TopTeamId",
                        column: x => x.TopTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BracketNodes_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BracketNodes_BottomTeamId",
                table: "BracketNodes",
                column: "BottomTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_BracketNodes_MatchId",
                table: "BracketNodes",
                column: "MatchId",
                unique: true,
                filter: "\"MatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BracketNodes_NextNodeId",
                table: "BracketNodes",
                column: "NextNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_BracketNodes_TopTeamId",
                table: "BracketNodes",
                column: "TopTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_BracketNodes_TournamentId_RoundNumber_Position",
                table: "BracketNodes",
                columns: new[] { "TournamentId", "RoundNumber", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BracketNodes");
        }
    }
}
