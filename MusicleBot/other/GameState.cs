using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MusicleBot.services;

namespace MusicleBot.other
{
    public class GameState
    {
        // Minimal track info for persistence
        public string TrackName { get; set; }
        public string TrackArtist { get; set; }
        public int TrackYear { get; set; }
        public int TrackDurationSeconds { get; set; }
        public int TrackPopularity { get; set; }

        public int SnippetSeconds { get; set; } = 1;
        public string AlbumName { get; set; }

        public bool TitleGuessed { get; set; }
        public bool ArtistGuessed { get; set; }
        public List<string> ArtistGenres { get; set; } = new();
        public int ArtistPopularity { get; set; }

        public ulong? TitleGuessedBy { get; set; }
        public ulong? ArtistGuessedBy { get; set; }

        public int SnippetIndex { get; set; } = 0;
        public static readonly int[] SnippetDurations = { 1, 3, 5, 10, 15 };
        public bool FinalSnippetSent { get; set; } = false;

        public string LastTrackKey { get; set; }
        public int TitlePoints { get; set; }
        public int ArtistPoints { get; set; }
        public bool BonusAwarded { get; set; }

        public string PlaylistUrl { get; set; }
        public string PlaylistName { get; set; } = "Unknown Playlist";
        public bool AutoPlay { get; set; }
        public int SnippetStartSeconds { get; set; }
        public List<HintType> HintOrder { get; set; } = new();
        public int HintsUsed { get; set; } = 0;
        public bool ExtraDataLoaded { get; set; }
        public HashSet<ulong> UsersCreditedThisRound { get; } = new();
        public HashSet<ulong> UsersHasGuessedRight { get; } = new();
        public HashSet<ulong> UsersWithIncorrectGuess { get; } = new();
        public int PointMultiplier { get; set; } = 1;
        public bool IsEnding { get; set; } = false;
        public Dictionary<ulong, int> HintsUsedByPlayer;
        public Dictionary<ulong, TimeSpan> GuessTimes;
        public DateTime RoundStartTime { get; set; }
        public int ConsecutiveTimeouts { get; set; } = 0;





        [JsonIgnore]
        public CancellationTokenSource TimeoutToken { get; set; }



        // Make dictionaries settable for JSON
        public Dictionary<ulong, Dictionary<string, int>> CorrectTitleGuesses { get; set; } = new();
        public Dictionary<ulong, Dictionary<string, int>> IncorrectTitleGuesses { get; set; } = new();
        public Dictionary<ulong, Dictionary<string, int>> CorrectArtistGuesses { get; set; } = new();
        public Dictionary<ulong, Dictionary<string, int>> IncorrectArtistGuesses { get; set; } = new();

        public HashSet<ulong> PlayersInGame { get; set; } = new();

        // Helper method to convert SpotifyService.TrackData to GameState
        public static GameState FromTrackData(SpotifyService.TrackData track)
        {
            return new GameState
            {
                TrackName = track.Name,
                TrackArtist = track.Artist,
                TrackDurationSeconds = track.DurationSeconds
            };
        }

        // Helper method to convert GameState to SpotifyService.TrackData
        public SpotifyService.TrackData ToTrackData()
        {
            return new SpotifyService.TrackData
            {
                Name = TrackName,
                Artist = TrackArtist,
                DurationSeconds = TrackDurationSeconds
            };
        }
    }

    //define hint types
    public enum HintType
    {
        Year,
        FirstLetter,
        Length,
        RandomLetters,
        Scrambled,
        Words,
        Popularity,
        Genres,
        Album,
        RevealVowels,
        EveryOtherLetter,
        WordCountPattern
    }

    // define round end reasons
    public enum RoundEndReason
    {
        Solved,
        GiveUp,
        Timeout,
        Break
    }

}
