using MusicleBot.other;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MusicleBot.services
{
    public static class GameHistoryService
    {
        private static readonly string FilePath = "gamehistory.json";

        // Saves the game history to a JSON file
        public static void Save(Dictionary<ulong, List<GameState>> gameHistory)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                // ignore Discord-specific objects if present
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            string json = JsonSerializer.Serialize(gameHistory, options);
            File.WriteAllText(FilePath, json);
        }

        // Loads the game history from a JSON file
        public static Dictionary<ulong, List<GameState>> Load()
        {
            if (!File.Exists(FilePath))
                return new Dictionary<ulong, List<GameState>>();

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<ulong, List<GameState>>>(json)
                   ?? new Dictionary<ulong, List<GameState>>();
        }
    }
}
