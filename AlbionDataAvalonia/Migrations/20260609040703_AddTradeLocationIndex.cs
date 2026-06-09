using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlbionDataAvalonia.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeLocationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Trades_AlbionServerId_LocationId_Deleted_DateTime",
                table: "Trades",
                columns: new[] { "AlbionServerId", "LocationId", "Deleted", "DateTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trades_AlbionServerId_LocationId_Deleted_DateTime",
                table: "Trades");
        }
    }
}
