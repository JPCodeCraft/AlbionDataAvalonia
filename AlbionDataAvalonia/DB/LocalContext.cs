using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Gathering.Models;
using AlbionDataAvalonia.Network.Models;
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

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string folderPath = AppData.LocalPath + "/data";
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            optionsBuilder.UseSqlite($"Data Source={folderPath}/afmdataclient.db");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GatheringCompletedSession>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.HasMany(x => x.Items)
                    .WithOne(x => x.Session)
                    .HasForeignKey(x => x.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => x.EndedAtUtc);
            });

            modelBuilder.Entity<GatheringCompletedSessionItem>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => x.SessionId);
            });

            modelBuilder.Entity<GatheringUnfinishedSessionCheckpoint>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => x.SessionId).IsUnique();
                entity.HasIndex(x => x.UpdatedAtUtc);
            });
        }
    }
}
