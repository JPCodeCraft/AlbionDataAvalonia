using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlbionDataAvalonia.Migrations
{
    /// <inheritdoc />
    public partial class initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlbionMails",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Received = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AlbionServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParcialAmount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalAmount = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    TotalSilver = table.Column<long>(type: "INTEGER", nullable: false),
                    UnitSilver = table.Column<long>(type: "INTEGER", nullable: false),
                    TaxesPercent = table.Column<double>(type: "REAL", nullable: false),
                    TotalTaxes = table.Column<long>(type: "INTEGER", nullable: false),
                    IsSet = table.Column<bool>(type: "INTEGER", nullable: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbionMails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlbionMails_AlbionServerId_LocationId_Type_Deleted",
                table: "AlbionMails",
                columns: new[] { "AlbionServerId", "LocationId", "Type", "Deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_AlbionMails_Id",
                table: "AlbionMails",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlbionMails_TotalSilver",
                table: "AlbionMails",
                column: "TotalSilver");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlbionMails");
        }
    }
}
