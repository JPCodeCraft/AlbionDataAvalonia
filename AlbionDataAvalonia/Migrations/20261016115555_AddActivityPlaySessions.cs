using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlbionDataAvalonia.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityPlaySessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlaySessionId",
                table: "ActivitySessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivityPlaySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Server = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterName = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    EndReason = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityPlaySessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySessions_PlaySessionId",
                table: "ActivitySessions",
                column: "PlaySessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlaySessions_Server_CharacterId_StartedAtUtc",
                table: "ActivityPlaySessions",
                columns: new[] { "Server", "CharacterId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlaySessions_StartedAtUtc",
                table: "ActivityPlaySessions",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlaySessions_State",
                table: "ActivityPlaySessions",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityPlaySessions");

            migrationBuilder.DropIndex(
                name: "IX_ActivitySessions_PlaySessionId",
                table: "ActivitySessions");

            migrationBuilder.DropColumn(
                name: "PlaySessionId",
                table: "ActivitySessions");
        }
    }
}
