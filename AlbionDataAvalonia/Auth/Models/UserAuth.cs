using System.ComponentModel.DataAnnotations;

namespace AlbionDataAvalonia.Auth.Models
{
    public class UserAuth
    {
        [Key]
        public string UserId { get; set; }
        public string RefreshToken { get; set; }
    }
}