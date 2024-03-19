using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.State.Events
{
    public class PlayerStateEventArgs : EventArgs
    {
        public AlbionData.Models.Location Location { get; set; }
        public string Name { get; set; }
        public AlbionServer? AlbionServer { get; set; }
        public int UploadQueueSize { get; set; }
        public PlayerStateEventArgs(AlbionData.Models.Location location, string name, AlbionServer? albionServer, int uploadQueueSize)
        {
            Location = location;
            Name = name;
            AlbionServer = albionServer;
            UploadQueueSize = uploadQueueSize;
        }
    }
}
