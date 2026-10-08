using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlbionDataAvalonia.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivitySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Server = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterName = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    RootMapId = table.Column<string>(type: "TEXT", nullable: false),
                    LocationName = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastRelevantAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ObservationStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ObservedDurationSeconds = table.Column<double>(type: "REAL", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    EndReason = table.Column<int>(type: "INTEGER", nullable: false),
                    HasLocalDeath = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasInferredGrouping = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPartial = table.Column<bool>(type: "INTEGER", nullable: false),
                    KnownInstancesJson = table.Column<string>(type: "TEXT", nullable: false),
                    VisitsJson = table.Column<string>(type: "TEXT", nullable: false),
                    PersonalSummaryJson = table.Column<string>(type: "TEXT", nullable: false),
                    PartySummaryJson = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedSummaryJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActivityEncounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    RawMapId = table.Column<string>(type: "TEXT", nullable: false),
                    LocationName = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCombatAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    EndReason = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonalSummaryJson = table.Column<string>(type: "TEXT", nullable: false),
                    PartySummaryJson = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedSummaryJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEncounters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityEncounters_ActivitySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ActivitySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActivityEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginKey = table.Column<string>(type: "TEXT", nullable: true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EncounterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Server = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<string>(type: "TEXT", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CharacterName = table.Column<string>(type: "TEXT", nullable: false),
                    CaptureOrder = table.Column<long>(type: "INTEGER", nullable: false),
                    CaptureGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    MapEpoch = table.Column<long>(type: "INTEGER", nullable: false),
                    GameTimestamp = table.Column<long>(type: "INTEGER", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Evidence = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRetracted = table.Column<bool>(type: "INTEGER", nullable: false),
                    RawMapId = table.Column<string>(type: "TEXT", nullable: false),
                    LocationName = table.Column<string>(type: "TEXT", nullable: false),
                    MapKind = table.Column<string>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<string>(type: "TEXT", nullable: true),
                    ParentMapId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceActorId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", nullable: false),
                    SourceGuild = table.Column<string>(type: "TEXT", nullable: false),
                    SourceAlliance = table.Column<string>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceRelationship = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetActorId = table.Column<string>(type: "TEXT", nullable: true),
                    TargetName = table.Column<string>(type: "TEXT", nullable: false),
                    TargetGuild = table.Column<string>(type: "TEXT", nullable: false),
                    TargetAlliance = table.Column<string>(type: "TEXT", nullable: false),
                    TargetKind = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetRelationship = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false),
                    SilverRaw = table.Column<long>(type: "INTEGER", nullable: false),
                    HasKnownNetSilver = table.Column<bool>(type: "INTEGER", nullable: false),
                    ParticipatedSelf = table.Column<bool>(type: "INTEGER", nullable: false),
                    ParticipatedParty = table.Column<bool>(type: "INTEGER", nullable: false),
                    MobUniqueName = table.Column<string>(type: "TEXT", nullable: true),
                    MobIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    ItemUniqueName = table.Column<string>(type: "TEXT", nullable: true),
                    ItemName = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    Quality = table.Column<int>(type: "INTEGER", nullable: true),
                    Quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    EstimatedUnitValue = table.Column<long>(type: "INTEGER", nullable: true),
                    PriceObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LootSourceName = table.Column<string>(type: "TEXT", nullable: false),
                    LootSourceKind = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityEvents_ActivityEncounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "ActivityEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityEvents_ActivitySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ActivitySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEncounters_SessionId_Number",
                table: "ActivityEncounters",
                columns: new[] { "SessionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEncounters_State",
                table: "ActivityEncounters",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_EncounterId_CapturedAtUtc_CaptureOrder",
                table: "ActivityEvents",
                columns: new[] { "EncounterId", "CapturedAtUtc", "CaptureOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_Kind_CapturedAtUtc",
                table: "ActivityEvents",
                columns: new[] { "Kind", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_Kind_Server_ItemId_Quality_EstimatedUnitValue",
                table: "ActivityEvents",
                columns: new[] { "Kind", "Server", "ItemId", "Quality", "EstimatedUnitValue" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_OriginKey",
                table: "ActivityEvents",
                column: "OriginKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_Server_CharacterId_CapturedAtUtc",
                table: "ActivityEvents",
                columns: new[] { "Server", "CharacterId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_SessionId_CapturedAtUtc_CaptureOrder",
                table: "ActivityEvents",
                columns: new[] { "SessionId", "CapturedAtUtc", "CaptureOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_SourceActorId_TargetActorId_SessionId_Kind",
                table: "ActivityEvents",
                columns: new[] { "SourceActorId", "TargetActorId", "SessionId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySessions_Server_CharacterId_StartedAtUtc",
                table: "ActivitySessions",
                columns: new[] { "Server", "CharacterId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySessions_StartedAtUtc",
                table: "ActivitySessions",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySessions_State",
                table: "ActivitySessions",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvents");

            migrationBuilder.DropTable(
                name: "ActivityEncounters");

            migrationBuilder.DropTable(
                name: "ActivitySessions");
        }
    }
}
