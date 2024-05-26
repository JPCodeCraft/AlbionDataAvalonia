using AlbionDataAvalonia.Network.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace AlbionDataAvalonia.DB
{
    public class LocalContext : DbContext
    {
        public DbSet<AlbionMail> AlbionMails { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string folderPath = AppData.LocalPath + "/data";
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            optionsBuilder.UseSqlite($"Data Source={folderPath}/afmdataclient.db");
        }
    }
}
