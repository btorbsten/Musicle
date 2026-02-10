using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MusicleBot.services
{
    internal class DefaultPlaylistService
    {

        private static readonly string FilePath = "user_default_playlists.json";

        // Loads the default playlists from the JSON file
        public static Dictionary<ulong, string> Load()
        {
            if (!File.Exists(FilePath))
            {
                return new();
            }

            return JsonSerializer.Deserialize<Dictionary<ulong, string>>(
           File.ReadAllText(FilePath)
            ) ?? new();
        }

        // Saves the default playlists to the JSON file
        public static void Save(Dictionary<ulong, string> data)
        {
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })
            );
        }

    }
}
