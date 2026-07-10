using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Gathering.Models;
using AlbionDataAvalonia.Legendary.Models;
using AlbionDataAvalonia.Network.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace AlbionDataAvalonia.DB
{
    public class LocalContext : DbContext
    {
        public DbSet<AlbionMail> AlbionMails { get; set; }
        public DbSet<Trade> Trades { get; set; }
        public DbSet<UserAuth> UserAuth { get; set; }
        public DbSet<GatheringCompletedSession> GatheringCompletedSessions { get; set; }
        public DbSet<GatheringCompletedSessionItem> GatheringCompletedSessionItems { get; set; }
        public DbSet<GatheringUnfinishedSessionCheckpoint> GatheringUnfinishedSessionCheckpoints { get; set; }
        public DbSet<LegendaryItem> LegendaryItems { get; set; }
        public DbSet<LegendaryItemTrait> LegendaryItemTraits { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            Directory.CreateDirectory(AppData.DataDirectoryPath);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = AppData.DatabasePath
            }.ToString();

            optionsBuilder.UseSqlite(connectionString);
        }

    }
}
