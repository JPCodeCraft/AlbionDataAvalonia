using AlbionDataAvalonia.Locations.Models;
using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.State.Events
{
    public class PlayerStateEventArgs : EventArgs
    {
        public AlbionLocation Location { get; set; }
        public string Name { get; set; }
        public AlbionServer? AlbionServer { get; set; }
        public bool IsInGame { get; set; }
        public bool HasEncryptedData { get; set; }
        public bool UploadToAfmOnly { get; set; }
        public PlayerStateEventArgs(AlbionLocation location, string name, AlbionServer? albionServer, bool isInGame, bool hasEncryptedData, bool uploadToAfmOnly)
        {
            Location = location;
            Name = name;
            AlbionServer = albionServer;
            IsInGame = isInGame;
            HasEncryptedData = hasEncryptedData;
            UploadToAfmOnly = uploadToAfmOnly;
        }
    }
}
