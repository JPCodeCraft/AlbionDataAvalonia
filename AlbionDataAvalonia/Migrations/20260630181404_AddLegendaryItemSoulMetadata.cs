using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlbionDataAvalonia.Migrations
{
    /// <inheritdoc />
    public partial class AddLegendaryItemSoulMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LegendaryItems_AlbionServerId_ObjectId",
                table: "LegendaryItems");

            migrationBuilder.AddColumn<long>(
                name: "AttunementSpent",
                table: "LegendaryItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Era",
                table: "LegendaryItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PvPFameGained",
                table: "LegendaryItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SoulId",
                table: "LegendaryItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SoulName",
                table: "LegendaryItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegendaryItems_AlbionServerId_ObjectId",
                table: "LegendaryItems",
                columns: new[] { "AlbionServerId", "ObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegendaryItems_AlbionServerId_SoulId",
                table: "LegendaryItems",
                columns: new[] { "AlbionServerId", "SoulId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LegendaryItems_AlbionServerId_ObjectId",
                table: "LegendaryItems");

            migrationBuilder.DropIndex(
                name: "IX_LegendaryItems_AlbionServerId_SoulId",
                table: "LegendaryItems");

            migrationBuilder.DropColumn(
                name: "AttunementSpent",
                table: "LegendaryItems");

            migrationBuilder.DropColumn(
                name: "Era",
                table: "LegendaryItems");

            migrationBuilder.DropColumn(
                name: "PvPFameGained",
                table: "LegendaryItems");

            migrationBuilder.DropColumn(
                name: "SoulId",
                table: "LegendaryItems");

            migrationBuilder.DropColumn(
                name: "SoulName",
                table: "LegendaryItems");

            migrationBuilder.CreateIndex(
                name: "IX_LegendaryItems_AlbionServerId_ObjectId",
                table: "LegendaryItems",
                columns: new[] { "AlbionServerId", "ObjectId" },
                unique: true);
        }
    }
}
