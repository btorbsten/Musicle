using MusicleBot.other;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MusicleBot.services
{
    internal static class DefaultPlaylistService
    {
        public static Dictionary<ulong, string> Load()
        {
            if (!File.Exists(DataPaths.DefaultPlaylists))
                return new();

            return JsonSerializer.Deserialize<Dictionary<ulong, string>>(
                File.ReadAllText(DataPaths.DefaultPlaylists)
            ) ?? new();
        }

        public static void Save(Dictionary<ulong, string> data)
        {
            File.WriteAllText(
                DataPaths.DefaultPlaylists,
                JsonSerializer.Serialize(
                    data,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
    }
}
