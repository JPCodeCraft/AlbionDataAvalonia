using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlbionDataAvalonia.Migrations
{
    /// <inheritdoc />
    public partial class AddLegendaryItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegendaryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AlbionServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ObjectId = table.Column<long>(type: "INTEGER", nullable: false),
                    ItemIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemUniqueName = table.Column<string>(type: "TEXT", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", nullable: false),
                    CrafterName = table.Column<string>(type: "TEXT", nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentDurability = table.Column<long>(type: "INTEGER", nullable: false),
                    EstimatedMarketValue = table.Column<long>(type: "INTEGER", nullable: false),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    HasItemDetails = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasLegendaryDetails = table.Column<bool>(type: "INTEGER", nullable: false),
                    Attunement = table.Column<long>(type: "INTEGER", nullable: true),
                    Strain = table.Column<double>(type: "REAL", nullable: true),
                    SeenByPlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LocationKind = table.Column<int>(type: "INTEGER", nullable: false),
                    RawLocationId = table.Column<string>(type: "TEXT", nullable: false),
                    LocationName = table.Column<string>(type: "TEXT", nullable: false),
                    ContainerObjectId = table.Column<long>(type: "INTEGER", nullable: true),
                    ContainerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PrivateContainerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContainerName = table.Column<string>(type: "TEXT", nullable: false),
                    ContainerIcon = table.Column<string>(type: "TEXT", nullable: false),
                    ContainerColor = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegendaryItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LegendaryItemTraits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LegendaryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    TraitId = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegendaryItemTraits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegendaryItemTraits_LegendaryItems_LegendaryItemId",
                        column: x => x.LegendaryItemId,
                        principalTable: "LegendaryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegendaryItems_AlbionServerId_ObjectId",
                table: "LegendaryItems",
                columns: new[] { "AlbionServerId", "ObjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegendaryItems_LastSeenAtUtc",
                table: "LegendaryItems",
                column: "LastSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LegendaryItemTraits_LegendaryItemId_Position",
                table: "LegendaryItemTraits",
                columns: new[] { "LegendaryItemId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegendaryItemTraits");

            migrationBuilder.DropTable(
                name: "LegendaryItems");
        }
    }
}
