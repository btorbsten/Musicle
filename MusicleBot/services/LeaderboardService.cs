using MusicleBot.other;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicleBot.services
{
    public static class LeaderboardService
    {
        public static void Save(Dictionary<ulong, Dictionary<ulong, UserScore>> leaderboards)
        {
            string json = JsonConvert.SerializeObject(leaderboards, Formatting.Indented);
            File.WriteAllText(DataPaths.Leaderboard, json);
        }

        public static Dictionary<ulong, Dictionary<ulong, UserScore>> Load()
        {
            if (!File.Exists(DataPaths.Leaderboard))
                return new();

            string json = File.ReadAllText(DataPaths.Leaderboard);
            return JsonConvert.DeserializeObject<
                Dictionary<ulong, Dictionary<ulong, UserScore>>
            >(json) ?? new();
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
