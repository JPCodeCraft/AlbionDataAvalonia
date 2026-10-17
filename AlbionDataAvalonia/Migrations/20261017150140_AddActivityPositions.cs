using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlbionDataAvalonia.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PositionX",
                table: "ActivityEvents",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PositionY",
                table: "ActivityEvents",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivityPathSegments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RawMapId = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PointCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Points = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityPathSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityPathSegments_ActivitySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ActivitySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPathSegments_SessionId_StartedAtUtc",
                table: "ActivityPathSegments",
                columns: new[] { "SessionId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityPathSegments");

            migrationBuilder.DropColumn(
                name: "PositionX",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "PositionY",
                table: "ActivityEvents");
        }
    }
}
