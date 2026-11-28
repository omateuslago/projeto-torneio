using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projeto_Campeonato.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Tournaments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_OwnerUserId",
                table: "Tournaments",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tournaments_AspNetUsers_OwnerUserId",
                table: "Tournaments",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tournaments_AspNetUsers_OwnerUserId",
                table: "Tournaments");

            migrationBuilder.DropIndex(
                name: "IX_Tournaments_OwnerUserId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Tournaments");
        }
    }
}
