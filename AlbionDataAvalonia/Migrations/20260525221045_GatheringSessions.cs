using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlbionDataAvalonia.Migrations
{
    /// <inheritdoc />
    public partial class GatheringSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GatheringCompletedSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActiveElapsedSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalEstimatedMarketValue = table.Column<long>(type: "INTEGER", nullable: false),
                    SilverPerHour = table.Column<long>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatheringCompletedSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GatheringUnfinishedSessionCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsPaused = table.Column<bool>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatheringUnfinishedSessionCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GatheringCompletedSessionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemUniqueName = table.Column<string>(type: "TEXT", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    EstimatedMarketValue = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalEstimatedMarketValue = table.Column<long>(type: "INTEGER", nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatheringCompletedSessionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GatheringCompletedSessionItems_GatheringCompletedSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "GatheringCompletedSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GatheringCompletedSessionItems_SessionId",
                table: "GatheringCompletedSessionItems",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_GatheringCompletedSessions_EndedAtUtc",
                table: "GatheringCompletedSessions",
                column: "EndedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GatheringUnfinishedSessionCheckpoints_SessionId",
                table: "GatheringUnfinishedSessionCheckpoints",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatheringUnfinishedSessionCheckpoints_UpdatedAtUtc",
                table: "GatheringUnfinishedSessionCheckpoints",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GatheringCompletedSessionItems");

            migrationBuilder.DropTable(
                name: "GatheringUnfinishedSessionCheckpoints");

            migrationBuilder.DropTable(
                name: "GatheringCompletedSessions");
        }
    }
}
