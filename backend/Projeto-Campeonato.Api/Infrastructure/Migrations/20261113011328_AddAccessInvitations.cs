using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projeto_Campeonato.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvitedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessInvitations", x => x.Id);
                    table.CheckConstraint("CK_AccessInvitations_Target", "(\"TournamentId\" IS NULL) <> (\"TeamId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_AccessInvitations_AspNetUsers_AcceptedByUserId",
                        column: x => x.AcceptedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessInvitations_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessInvitations_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessInvitations_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessInvitations_AcceptedByUserId",
                table: "AccessInvitations",
                column: "AcceptedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessInvitations_CreatedByUserId",
                table: "AccessInvitations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessInvitations_NormalizedEmail",
                table: "AccessInvitations",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AccessInvitations_TeamId",
                table: "AccessInvitations",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessInvitations_TeamId_NormalizedEmail",
                table: "AccessInvitations",
                columns: new[] { "TeamId", "NormalizedEmail" },
                unique: true,
                filter: "\"Active\" = TRUE AND \"TeamId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccessInvitations_TournamentId",
                table: "AccessInvitations",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessInvitations_TournamentId_NormalizedEmail",
                table: "AccessInvitations",
                columns: new[] { "TournamentId", "NormalizedEmail" },
                unique: true,
                filter: "\"Active\" = TRUE AND \"TournamentId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessInvitations");
        }
    }
}
