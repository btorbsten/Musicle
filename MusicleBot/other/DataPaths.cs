using System;
using System.IO;

namespace MusicleBot.other
{
    public static class DataPaths
    {
        public static readonly string BaseDir =
            Path.Combine(AppContext.BaseDirectory, "data");

        static DataPaths()
        {
            Directory.CreateDirectory(BaseDir);
        }

        public static string Config => Path.Combine(BaseDir, "config.json");
        public static string Leaderboard => Path.Combine(BaseDir, "leaderboard.json");
        public static string GameHistory => Path.Combine(BaseDir, "gamehistory.json");
        public static string DefaultPlaylists => Path.Combine(BaseDir, "user_default_playlists.json");
    }
}
