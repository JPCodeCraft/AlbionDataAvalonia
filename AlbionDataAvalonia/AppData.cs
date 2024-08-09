using Serilog.Core;
using System;
using System.IO;

namespace AlbionDataAvalonia
{
    public static class AppData
    {
        public static string LocalPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMDataClient");
        public static LoggingLevelSwitch? ListSinkLevelSwitch { get; set; }
    }
}