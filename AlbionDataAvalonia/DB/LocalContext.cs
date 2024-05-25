using AlbionDataAvalonia.Network.Models;
using Microsoft.EntityFrameworkCore;

namespace AlbionDataAvalonia.DB
{
    public class LocalContext : DbContext
    {
        public DbSet<AlbionMail> AlbionMails { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={AppData.LocalPath}/afmdataclient.db");
        }
    }
}
