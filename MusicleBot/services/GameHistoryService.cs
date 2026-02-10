using MusicleBot.other;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MusicleBot.services
{
    public static class GameHistoryService
    {
        public static void Save(Dictionary<ulong, List<GameState>> gameHistory)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition =
                    System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            string json = JsonSerializer.Serialize(gameHistory, options);
            File.WriteAllText(DataPaths.GameHistory, json);
        }

        public static Dictionary<ulong, List<GameState>> Load()
        {
            if (!File.Exists(DataPaths.GameHistory))
                return new();

            string json = File.ReadAllText(DataPaths.GameHistory);
            return JsonSerializer.Deserialize<
                Dictionary<ulong, List<GameState>>
            >(json) ?? new();
        }
    }
}
