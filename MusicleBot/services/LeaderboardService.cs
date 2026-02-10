using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace MusicleBot.services
{
    public static class LeaderboardService
    {
        private static readonly string FilePath = "leaderboard.json";

        // Saves the leaderboards to a JSON file
        public static void Save(Dictionary<ulong, Dictionary<ulong, UserScore>> leaderboards)
        {
            string json = JsonConvert.SerializeObject(leaderboards, Formatting.Indented);
            File.WriteAllText(FilePath, json);
        }

        // Loads the leaderboards from a JSON file
        public static Dictionary<ulong, Dictionary<ulong, UserScore>> Load()
        {
            if (!File.Exists(FilePath))
                return new Dictionary<ulong, Dictionary<ulong, UserScore>>();

            string json = File.ReadAllText(FilePath);
            return JsonConvert.DeserializeObject<Dictionary<ulong, Dictionary<ulong, UserScore>>>(json)
                   ?? new Dictionary<ulong, Dictionary<ulong, UserScore>>();
        }
    }

    // User score data structure
    public class UserScore
    {
        public int Points { get; set; } = 0;
        public int CurrentStreak { get; set; }
        public int HighestStreak { get; set; }
        public int TotalBonusPoints { get; set; }
        public int TotalHintsUsed { get; set; }
        public bool AnnouncedNewBestThisRun { get; set; }
        public TimeSpan? FastestCorrectGuess;
        public int TotalCorrectGuesses { get; set; }
        public TimeSpan TotalCorrectGuessTime { get; set; }
        public int TotalGuessesMade { get; set; }

    }
}
