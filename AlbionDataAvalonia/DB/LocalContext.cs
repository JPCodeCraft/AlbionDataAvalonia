using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Gathering.Models;
using AlbionDataAvalonia.Legendary.Models;
using AlbionDataAvalonia.Network.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
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
        public DbSet<ActivityPlaySession> ActivityPlaySessions { get; set; }
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
            modelBuilder.Entity<ActivityPlaySession>(entity =>
            {
                entity.ToTable("ActivityPlaySessions");
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => new { x.Server, x.CharacterId, x.StartedAtUtc });
                entity.HasIndex(x => x.StartedAtUtc);
                entity.HasIndex(x => x.State);
            });
            // Runs refer to their play session without a constraint; the journal writer
            // keeps the two consistent and removes play sessions left without runs.
            modelBuilder.Entity<ActivitySession>(entity =>
            {
                entity.ToTable("ActivitySessions");
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => new { x.Server, x.CharacterId, x.StartedAtUtc });
                entity.HasIndex(x => x.StartedAtUtc);
                entity.HasIndex(x => x.State);
                entity.HasIndex(x => x.PlaySessionId);
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

            // SQLite preserves the UTC ticks but drops DateTime.Kind. Restore it on reads
            // so historical chart ranges and event times are not shifted a second time.
            var utcDateTime = new ValueConverter<DateTime, DateTime>(
                value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            foreach (var activityType in new[] { typeof(ActivityPlaySession), typeof(ActivitySession), typeof(ActivityEncounter), typeof(ActivityEvent) })
            {
                foreach (var property in modelBuilder.Entity(activityType).Metadata.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                        property.SetValueConverter(utcDateTime);
                }
            }
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
