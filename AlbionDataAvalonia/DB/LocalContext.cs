using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Activity;
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
        public DbSet<ActivitySession> ActivitySessions { get; set; }
        public DbSet<ActivityEncounter> ActivityEncounters { get; set; }
        public DbSet<ActivityEvent> ActivityEvents { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDbFunction(typeof(ActivityDatabaseFunctions).GetMethod(nameof(ActivityDatabaseFunctions.JulianDay))!)
                .HasName("julianday").IsBuiltIn();
            modelBuilder.HasDbFunction(typeof(ActivityDatabaseFunctions).GetMethod(nameof(ActivityDatabaseFunctions.JsonLong))!)
                .HasName("json_extract").IsBuiltIn();
            modelBuilder.Entity<ActivitySession>(entity =>
            {
                entity.ToTable("ActivitySessions");
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => new { x.Server, x.CharacterId, x.StartedAtUtc });
                entity.HasIndex(x => x.StartedAtUtc);
                entity.HasIndex(x => x.State);
            });
            modelBuilder.Entity<ActivityEncounter>(entity =>
            {
                entity.ToTable("ActivityEncounters");
                entity.HasKey(x => x.Id);
                entity.Ignore(x => x.CombatDurationSeconds);
                entity.HasOne<ActivitySession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => new { x.SessionId, x.Number }).IsUnique();
                entity.HasIndex(x => x.State);
            });
            modelBuilder.Entity<ActivityEvent>(entity =>
            {
                entity.ToTable("ActivityEvents");
                entity.HasKey(x => x.Id);
                entity.HasOne<ActivitySession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<ActivityEncounter>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => x.OriginKey).IsUnique();
                entity.HasIndex(x => new { x.SessionId, x.CapturedAtUtc, x.CaptureOrder });
                entity.HasIndex(x => new { x.EncounterId, x.CapturedAtUtc, x.CaptureOrder });
                entity.HasIndex(x => new { x.Kind, x.CapturedAtUtc });
                entity.HasIndex(x => new { x.Server, x.CharacterId, x.CapturedAtUtc });
                entity.HasIndex(x => new { x.Kind, x.Server, x.ItemId, x.Quality, x.EstimatedUnitValue });
                entity.HasIndex(x => new { x.SourceActorId, x.TargetActorId, x.SessionId, x.Kind });
            });
        }

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
