using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using MusicleBot.other;
using MusicleBot.services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MusicleBot.commands
{
    public class MusicCommands : BaseCommandModule
    {
        private readonly SpotifyService _spotify;

        private static readonly Dictionary<ulong, GameState> ActiveGames = new();


        private const string DEFAULT_PLAYLIST =
            "https://open.spotify.com/playlist/51cZLTqi8xgNWTH0AkIqVU";

        public MusicCommands(SpotifyService spotify)
        {
            _spotify = spotify;
        }


        private static readonly Dictionary<ulong, DateTime> PassCooldowns = new();
        private static readonly TimeSpan PASS_COOLDOWN = TimeSpan.FromSeconds(3);

        private static readonly Random Rng = new();


        // Starts a new game in the channel
        [Command("play")]
        public async Task Play(CommandContext ctx, string playlistUrl = null)
        {
            string url;

            if (!string.IsNullOrWhiteSpace(playlistUrl))
            {
                url = playlistUrl;
            }
            else if (Program.UserDefaultPlaylist.TryGetValue(ctx.User.Id, out var userDefault))
            {
                // User has a default playlist
                url = userDefault;
            }
            else
            {
                // Fallback to global default
                url = DEFAULT_PLAYLIST;
            }



            var tracks = await _spotify.GetTracksFromPlaylist(url);
            if (tracks.Count == 0)
            {
                await ctx.RespondAsync("No tracks found in that playlist.");
                return;
            }


            ActiveGames.TryGetValue(ctx.Channel.Id, out var previousGame);
            var playlistName = await _spotify.GetPlaylistName(url);


            SpotifyService.TrackData track;

            do
            {
                track = tracks[Rng.Next(tracks.Count)];
            }
            while (
                previousGame?.LastTrackKey ==
                $"{track.Name}|{track.Artist}"
                && tracks.Count > 1
            );

            var game = new GameState
            {
                TrackName = track.Name,
                TrackArtist = track.Artist,
                TrackYear = track.Year,
                TrackDurationSeconds = track.DurationSeconds,
                SnippetIndex = 0,
                PlaylistUrl = url,
                PlaylistName = playlistName,
                AutoPlay = true,
                LastTrackKey = $"{track.Name}|{track.Artist}",
                ArtistGenres = track.ArtistGenres,
                TrackPopularity = track.TrackPopularity,
                ArtistPopularity = track.ArtistPopularity,
                AlbumName = track.AlbumName
            };

            game.PointMultiplier = RollPointMultiplier();

            game.HintOrder = Enum.GetValues<HintType>()
            .OrderBy(_ => Rng.Next())
            .ToList();
            game.HintsUsed = 0;



            ActiveGames[ctx.Channel.Id] = game;

            int seconds = GameState.SnippetDurations[0];
            await SendSnippet(ctx, track, seconds);


        }



        // User passes to the next snippet length
        [Command("pass"), Aliases("p")]
        public async Task Pass(CommandContext ctx)
        {
            if (!ActiveGames.TryGetValue(ctx.Channel.Id, out var game))
            {
                await ctx.RespondAsync("No active game.");
                return;
            }

            // Check cooldown
            if (PassCooldowns.TryGetValue(ctx.Channel.Id, out var lastPass))
            {
                if (DateTime.UtcNow - lastPass < PASS_COOLDOWN)
                    return;
            }

            PassCooldowns[ctx.Channel.Id] = DateTime.UtcNow;


            if (game.SnippetIndex >= GameState.SnippetDurations.Length - 1)
                return;

            game.SnippetIndex++;
            game.SnippetSeconds = GameState.SnippetDurations[game.SnippetIndex];

            await SendSnippet(ctx, game.ToTrackData(), game.SnippetSeconds);

            if (game.SnippetIndex == GameState.SnippetDurations.Length - 1)
            {
                await ctx.Channel.SendMessageAsync("⚠️ Final snippet!");
                game.FinalSnippetSent = true;
            }
        }

        // User makes a guess
        [Command("guess"), Aliases("g")]
        public async Task Guess(CommandContext ctx, [RemainingText] string userGuess)
        {
            if (string.IsNullOrWhiteSpace(userGuess))
            {
                await ctx.Message.CreateReactionAsync(DiscordEmoji.FromUnicode("❌"));
                return;
            }

            if (!ActiveGames.TryGetValue(ctx.Channel.Id, out var game) || game.IsEnding)
            {
                await ctx.Message.CreateReactionAsync(DiscordEmoji.FromUnicode("❌"));
                return;
            }

            game.PlayersInGame.Add(ctx.User.Id);

            string guess = Normalize(userGuess);
            string trackTitle = Normalize(game.TrackName);
            string trackArtist = Normalize(game.TrackArtist);

            bool correct = false;
            bool alreadyCountedCorrectThisGuess = false;

            EnsureScore(ctx.Guild.Id, ctx.User.Id);
            var score = Program.Leaderboards[ctx.Guild.Id][ctx.User.Id];
            score.TotalGuessesMade++;

            bool gotTitleThisGuess = false;
            bool gotArtistThisGuess = false;

            // checks if guess matches title
            if (!game.TitleGuessed && IsCloseMatch(guess, trackTitle, 0.65))
            {
                int points = GetPointsForSnippet(game.SnippetSeconds) * game.PointMultiplier;
                game.TitlePoints = points;
                game.TitleGuessed = true;
                game.TitleGuessedBy = ctx.User.Id;

                Program.Leaderboards[ctx.Guild.Id][ctx.User.Id].Points += points;

                if (!game.UsersCreditedThisRound.Contains(ctx.User.Id) && !alreadyCountedCorrectThisGuess)
                {
                    await HandleCorrectGuess(ctx, ctx.User.Id, score, game);
                    alreadyCountedCorrectThisGuess = true;
                    game.UsersCreditedThisRound.Add(ctx.User.Id);
                }

                gotTitleThisGuess = true;

                // Track correct title guesses
                if (!game.CorrectTitleGuesses.ContainsKey(ctx.User.Id))
                    game.CorrectTitleGuesses[ctx.User.Id] = new Dictionary<string, int>();
                if (!game.CorrectTitleGuesses[ctx.User.Id].ContainsKey(game.TrackName))
                    game.CorrectTitleGuesses[ctx.User.Id][game.TrackName] = 0;
                game.CorrectTitleGuesses[ctx.User.Id][game.TrackName]++;
            }
            else
            {
                // Track incorrect title guesses
                if (!game.IncorrectTitleGuesses.ContainsKey(ctx.User.Id))
                    game.IncorrectTitleGuesses[ctx.User.Id] = new Dictionary<string, int>();
                if (!game.IncorrectTitleGuesses[ctx.User.Id].ContainsKey(game.TrackName))
                    game.IncorrectTitleGuesses[ctx.User.Id][game.TrackName] = 0;
                game.IncorrectTitleGuesses[ctx.User.Id][game.TrackName]++;
            }

            // checks if guess matches artist
            if (!game.ArtistGuessed && IsCloseMatch(guess, trackArtist, 0.65))
            {
                int points = GetPointsForSnippet(game.SnippetSeconds) * game.PointMultiplier;
                game.ArtistPoints = points;
                game.ArtistGuessed = true;
                game.ArtistGuessedBy = ctx.User.Id;

                Program.Leaderboards[ctx.Guild.Id][ctx.User.Id].Points += points;

                if (!game.UsersCreditedThisRound.Contains(ctx.User.Id) && !alreadyCountedCorrectThisGuess)
                {
                    await HandleCorrectGuess(ctx, ctx.User.Id, score, game);
                    alreadyCountedCorrectThisGuess = true;
                    game.UsersCreditedThisRound.Add(ctx.User.Id);
                }

                gotArtistThisGuess = true;

                // Track correct artist guesses
                if (!game.CorrectArtistGuesses.ContainsKey(ctx.User.Id))
                    game.CorrectArtistGuesses[ctx.User.Id] = new Dictionary<string, int>();
                if (!game.CorrectArtistGuesses[ctx.User.Id].ContainsKey(game.TrackArtist))
                    game.CorrectArtistGuesses[ctx.User.Id][game.TrackArtist] = 0;
                game.CorrectArtistGuesses[ctx.User.Id][game.TrackArtist]++;
            }
            else
            {
                // Track incorrect artist guesses
                if (!game.IncorrectArtistGuesses.ContainsKey(ctx.User.Id))
                    game.IncorrectArtistGuesses[ctx.User.Id] = new Dictionary<string, int>();
                if (!game.IncorrectArtistGuesses[ctx.User.Id].ContainsKey(game.TrackArtist))
                    game.IncorrectArtistGuesses[ctx.User.Id][game.TrackArtist] = 0;
                game.IncorrectArtistGuesses[ctx.User.Id][game.TrackArtist]++;
            }

            correct = gotTitleThisGuess || gotArtistThisGuess;

            await ctx.Message.CreateReactionAsync(DiscordEmoji.FromUnicode(correct ? "✅" : "❌"));

            var member = await ctx.Guild.GetMemberAsync(ctx.User.Id);

            // Only send individual TITLE/ARTIST messages if they didn't guess both in one guess
            if (!(gotTitleThisGuess && gotArtistThisGuess))
            {
                if (gotTitleThisGuess)
                    await ctx.Channel.SendMessageAsync($"**{member.DisplayName}** got the **TITLE**! (+{game.TitlePoints})");
                if (gotArtistThisGuess)
                    await ctx.Channel.SendMessageAsync($"**{member.DisplayName}** got the **ARTIST**! (+{game.ArtistPoints})");
            }

            if (correct)
            {
                game.UsersHasGuessedRight.Add(ctx.User.Id);
                game.UsersWithIncorrectGuess.Remove(ctx.User.Id);
            }
            else if (!game.UsersHasGuessedRight.Contains(ctx.User.Id))
            {
                game.UsersWithIncorrectGuess.Add(ctx.User.Id);
            }

            // Check for round completion
            if (game.TitleGuessed && game.ArtistGuessed)
            {
                if (game.IsEnding) return;

                game.IsEnding = true;
                game.ConsecutiveTimeouts = 0;


                string titleGuesser = $"{(await ctx.Guild.GetMemberAsync(game.TitleGuessedBy.Value)).DisplayName} (+{game.TitlePoints})";
                string artistGuesser = $"{(await ctx.Guild.GetMemberAsync(game.ArtistGuessedBy.Value)).DisplayName} (+{game.ArtistPoints})";

                await ctx.Channel.SendMessageAsync(
                    $"✅ **Solved!**\n" +
                    $"Title: {game.TrackName} — **{titleGuesser}**\n" +
                    $"Artist: {game.TrackArtist} — **{artistGuesser}**"
                );

                // Award bonus points if same user guessed both
                if (!game.BonusAwarded &&
                    game.TitleGuessedBy == game.ArtistGuessedBy)
                {
                    int bonusPoints = game.PointMultiplier;
                    var solverId = game.TitleGuessedBy.Value;
                    Program.Leaderboards[ctx.Guild.Id][solverId].Points += bonusPoints;
                    game.BonusAwarded = true;
                    LeaderboardService.Save(Program.Leaderboards);

                    await ctx.Channel.SendMessageAsync(
                        $"Bonus! **{member.DisplayName}** got **title & artist (+{bonusPoints})**"
                    );
                }

                await HandleStreakLosses(ctx, game, RoundEndReason.Solved);

                game.TimeoutToken?.Cancel();
                var finishedGame = game;
                ActiveGames.Remove(ctx.Channel.Id);

                if (!Program.GameHistory.ContainsKey(ctx.Guild.Id))
                    Program.GameHistory[ctx.Guild.Id] = new List<GameState>();
                Program.GameHistory[ctx.Guild.Id].Add(finishedGame);
                GameHistoryService.Save(Program.GameHistory);

                await StartNextRound(ctx, finishedGame);
            }
        }


        // User gives up, reveals the answer, auto-play continues
        [Command("giveup"), Aliases("gu")]
        public async Task GiveUpCommand(CommandContext ctx)
        {
            if (!ActiveGames.TryGetValue(ctx.Channel.Id, out var game))
            {
                await ctx.RespondAsync("No active game.");
                return;
            }
            if (game.IsEnding)
                return;

            game.IsEnding = true;
            game.ConsecutiveTimeouts = 0;

            game.PlayersInGame.Add(ctx.User.Id);

            string titleLine;
            if (game.TitleGuessed)
            {
                var member = await ctx.Guild.GetMemberAsync(game.TitleGuessedBy.Value);
                titleLine = $"**Title:** {game.TrackName} — **{member.DisplayName}** (+{game.TitlePoints})";
            }
            else
            {
                titleLine = $"**Title:** {game.TrackName} — ❌ Not guessed";
            }

            string artistLine;
            if (game.ArtistGuessed)
            {
                var member = await ctx.Guild.GetMemberAsync(game.ArtistGuessedBy.Value);
                artistLine = $"**Artist:** {game.TrackArtist} — **{member.DisplayName}** (+{game.ArtistPoints})";
            }
            else
            {
                artistLine = $"**Artist:** {game.TrackArtist} — ❌ Not guessed";
            }


            await ctx.Channel.SendMessageAsync(
                $"**Answer:**\n" +
                $"{titleLine}\n" +
                $"{artistLine}"
            );

            game.TimeoutToken?.Cancel();
            var finishedGame = game;
            ActiveGames.Remove(ctx.Channel.Id);



            await HandleStreakLosses(ctx, game, RoundEndReason.GiveUp);



            if (!Program.GameHistory.ContainsKey(ctx.Guild.Id))
                Program.GameHistory[ctx.Guild.Id] = new();

            Program.GameHistory[ctx.Guild.Id].Add(finishedGame);
            GameHistoryService.Save(Program.GameHistory);


            await StartNextRound(ctx, finishedGame);

        }

        // Displays the server leaderboard by points
        [Command("leaderboard"), Aliases("lb")]
        public async Task Leaderboard(CommandContext ctx)
        {
            if (!Program.Leaderboards.TryGetValue(ctx.Guild.Id, out var board) || board.Count == 0)
            {
                await ctx.RespondAsync("No scores yet.");
                return;
            }

            var embed = new DiscordEmbedBuilder
            {
                Title = "Leaderboard",
                Color = DiscordColor.Gold
            };

            // Get entries sorted by points
            var entries = board
                .OrderByDescending(x => x.Value.Points)
                .Select(async x =>
                {
                    var user = await ctx.Guild.GetMemberAsync(x.Key);
                    return (Mention: user.Mention, Points: x.Value.Points);
                })
                .Select(t => t.Result)
                .ToList();


            int maxLength = entries.Max(e => e.Mention.Length);


            var lines = entries
                .Select((e, index) => $"{index + 1}. {e.Mention.PadRight(maxLength)} — {e.Points} points");

            embed.Description = string.Join("\n", lines);

            await ctx.Channel.SendMessageAsync(embed: embed);
        }


        // Stops the current game, turns off autoplay and reveals the answer
        [Command("break"), Aliases("b", "stop")]
        public async Task Break(CommandContext ctx)
        {
            if (!ActiveGames.TryGetValue(ctx.Channel.Id, out var game))
            {
                await ctx.RespondAsync("No active game.");
                return;
            }

            // Stop auto-play
            game.AutoPlay = false;
            game.ConsecutiveTimeouts = 0;

            // Cancel the current timeout
            game.TimeoutToken?.Cancel();
            game.TimeoutToken = null;

            // Mark game as ending
            if (!game.IsEnding)
            {
                game.IsEnding = true;

                // Send auto-play stopped message first
                await ctx.RespondAsync("⏸️ Auto-play stopped.");

                // Then reveal the answer like ;giveup
                string titleLine = game.TitleGuessed
                    ? $"**Title:** {game.TrackName} — <@{game.TitleGuessedBy}> (+{game.TitlePoints})"
                    : $"**Title:** {game.TrackName} — ❌ Not guessed";

                string artistLine = game.ArtistGuessed
                    ? $"**Artist:** {game.TrackArtist} — <@{game.ArtistGuessedBy}> (+{game.ArtistPoints})"
                    : $"**Artist:** {game.TrackArtist} — ❌ Not guessed";

                await ctx.Channel.SendMessageAsync(
                    $"**Answer:**\n{titleLine}\n{artistLine}"
                );

                // Handle streak losses for anyone who hasn't guessed correctly
                await HandleStreakLosses(ctx, game, RoundEndReason.Break);

                // Save to history
                ActiveGames.Remove(ctx.Channel.Id);

                if (!Program.GameHistory.ContainsKey(ctx.Guild.Id))
                    Program.GameHistory[ctx.Guild.Id] = new List<GameState>();

                Program.GameHistory[ctx.Guild.Id].Add(game);
                GameHistoryService.Save(Program.GameHistory);
            }
        }



        // Sets the user's default playlist
        [Command("setplaylist"), Aliases("sp")]
        public async Task SetDefaultPlaylist(CommandContext ctx, string playlistUrl)
        {
            if (string.IsNullOrWhiteSpace(playlistUrl))
            {
                await ctx.RespondAsync("You must provide a Spotify playlist URl.");
                return;
            }

            Program.UserDefaultPlaylist[ctx.User.Id] = playlistUrl;
            DefaultPlaylistService.Save(Program.UserDefaultPlaylist);

            await ctx.RespondAsync("Your default playlist has been set.");
        }


        // Removes the user's default playlist
        [Command("removeplaylist"), Aliases("rp")]
        public async Task RemoveDefaultPlaylist(CommandContext ctx)
        {
            if (!Program.UserDefaultPlaylist.Remove(ctx.User.Id))
            {
                await ctx.RespondAsync("You don't have a default playlist set.");
                return;
            }

            DefaultPlaylistService.Save(Program.UserDefaultPlaylist);
            await ctx.RespondAsync("Your default playlist has been removed.");
        }

        // Displays the user's default playlist
        [Command("myplaylist"), Aliases("mp")]
        public async Task MyPlaylist(CommandContext ctx)
        {
            if (!Program.UserDefaultPlaylist.TryGetValue(ctx.User.Id, out var url))
            {
                await ctx.RespondAsync("You don’t have a default playlist set.");
                return;
            }

            await ctx.RespondAsync($"Your default playlist:\n{url}");
        }


        // Displays individual user statistics
        [Command("stats")]
        public async Task Stats(CommandContext ctx, DiscordUser user = null)
        {
            user ??= ctx.User;

            if (!Program.Leaderboards.ContainsKey(ctx.Guild.Id) ||
                !Program.Leaderboards[ctx.Guild.Id].ContainsKey(user.Id))
            {
                await ctx.RespondAsync($"{user.Username} has no stats yet!");
                return;
            }

            var userScore = Program.Leaderboards[ctx.Guild.Id][user.Id];
            int totalPoints = userScore.Points;
            int longestStreak = userScore.HighestStreak;


            // All games where user participated
            var userGames = Program.GameHistory.ContainsKey(ctx.Guild.Id)
                ? Program.GameHistory[ctx.Guild.Id]
                    .Where(g => g.PlayersInGame.Contains(user.Id))
                    .ToList()
                : new List<GameState>();

            int gamesPlayed = userGames.Count;

            int correctTitleGuesses = userGames.Count(g => g.TitleGuessedBy == user.Id);
            int correctArtistGuesses = userGames.Count(g => g.ArtistGuessedBy == user.Id);
            int totalCorrectGuesses = correctTitleGuesses + correctArtistGuesses;


            //correct guess percentage
            double correctPercentage = userScore.TotalGuessesMade > 0
                ? Math.Round(
                    (double)totalCorrectGuesses / userScore.TotalGuessesMade * 100,
                    1
                  )
                : 0;

            // Average points per game
            double avgPointsPerGame = gamesPlayed > 0
                ? Math.Round((double)totalPoints / gamesPlayed, 1)
                : 0;

            // amount of bonus points
            int bonusPoints = userGames.Count(g =>
                g.TitleGuessedBy == user.Id &&
                g.ArtistGuessedBy == user.Id &&
                g.BonusAwarded
            );

            // Top Playlist
            var topPlaylistGroup = userGames
                .GroupBy(g => new { g.PlaylistUrl, g.PlaylistName })
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            string topPlaylistText = topPlaylistGroup != null
                ? $"{topPlaylistGroup.Count()} plays — {topPlaylistGroup.Key.PlaylistName} ({topPlaylistGroup.Key.PlaylistUrl})"
                : "N/A";

            // Favorite Artist
            var favoriteArtistGroup = userGames
                .Where(g => g.ArtistGuessedBy == user.Id)
                .GroupBy(g => g.TrackArtist)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            string favoriteArtist = favoriteArtistGroup != null
                ? $"{favoriteArtistGroup.Key} ({favoriteArtistGroup.Count()})"
                : "N/A";

            // Most Missed Artist
            var missedArtistGroup = userGames
                .Where(g => g.ArtistGuessedBy != user.Id)
                .GroupBy(g => g.TrackArtist)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            string missedArtist = missedArtistGroup != null
                ? $"{missedArtistGroup.Key} ({missedArtistGroup.Count()})"
                : "N/A";

            // Favorite Song
            var favoriteSongGroup = userGames
                .Where(g => g.TitleGuessedBy == user.Id)
                .GroupBy(g => g.TrackName)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            string favoriteSong = favoriteSongGroup != null
                ? $"{favoriteSongGroup.Key} ({favoriteSongGroup.Count()})"
                : "N/A";

            // Most Missed Song
            var missedSongGroup = userGames
                .Where(g => g.TitleGuessedBy != user.Id)
                .GroupBy(g => g.TrackName)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            string missedSong = missedSongGroup != null
                ? $"{missedSongGroup.Key} ({missedSongGroup.Count()})"
                : "N/A";

            // Rank
            var guildLeaderboard = Program.Leaderboards[ctx.Guild.Id]
                .OrderByDescending(x => x.Value.Points)
                .ToList();

            int rankIndex = guildLeaderboard.FindIndex(x => x.Key == user.Id);
            int totalUsers = guildLeaderboard.Count;
            string rankDisplay = rankIndex >= 0 ? $"{rankIndex + 1}/{totalUsers}" : $"0/{totalUsers}";

            // Total hints used
            int totalHintsUsed = userScore.TotalHintsUsed;

            // Average hints per game
            double avgHintsPerGame = gamesPlayed > 0
                ? Math.Round((double)totalHintsUsed / gamesPlayed, 2)
                : 0;

            // Fastest correct guess
            string fastestGuessText = userScore.FastestCorrectGuess.HasValue
                ? $"{userScore.FastestCorrectGuess.Value.TotalSeconds:0.00}s"
                : "N/A";

            // Average correct guess time
            string avgCorrectGuessTimeText = userScore.TotalCorrectGuesses > 0
                    ? $"{TimeSpan.FromSeconds(userScore.TotalCorrectGuessTime.TotalSeconds / userScore.TotalCorrectGuesses).TotalSeconds:0.00}s"
                    : "N/A";



            // Embed
            var embed = new DiscordEmbedBuilder
            {
                Title = $"{user.Username}'s Stats",
                Color = DiscordColor.Blurple
            };


            embed.AddField("Current Rank", rankDisplay, true);
            embed.AddField("Total Points", totalPoints.ToString(), true);
            embed.AddField("Longest Streak", longestStreak.ToString(), true);
            embed.AddField("Total Bonus Points", bonusPoints.ToString(), true);
            embed.AddField("Games Played", gamesPlayed.ToString(), true);
            embed.AddField("Guess Accuracy", $"{correctPercentage}%", true);
            embed.AddField("Avg Points/Game", avgPointsPerGame.ToString(), true);
            embed.AddField("Avg Hints/Game", avgHintsPerGame.ToString(), true);
            embed.AddField("Most Guessed Artist", favoriteArtist, true);
            embed.AddField("Most Missed Artist", missedArtist, true);
            embed.AddField("Most Guessed Song", favoriteSong, true);
            embed.AddField("Most Missed Song", missedSong, true);
            embed.AddField("Total Hints Used", totalHintsUsed.ToString(), true);
            embed.AddField("Fastest Correct Guess", fastestGuessText, true);
            embed.AddField("Avg Correct Guess Time", avgCorrectGuessTimeText, true);
            embed.AddField("Most Played Playlist", topPlaylistText, false);

            await ctx.Channel.SendMessageAsync(embed: embed);
        }

        // Displays server-wide statistics
        [Command("serverstats"), Aliases("sstats")]
        public async Task ServerStats(CommandContext ctx)
        {
            if (!Program.GameHistory.TryGetValue(ctx.Guild.Id, out var games) || games.Count == 0)
            {
                await ctx.RespondAsync("No server stats yet!");
                return;
            }

            // Aggregate stats
            var leaderboard = Program.Leaderboards.GetValueOrDefault(ctx.Guild.Id);
            int totalGames = games.Count;
            int totalGuesses = leaderboard?.Values.Sum(u => u.TotalGuessesMade) ?? 0;
            int totalCorrectGuesses = leaderboard?.Values.Sum(u => u.TotalCorrectGuesses) ?? 0;
            double accuracy = totalGuesses > 0
                ? Math.Round((double)totalCorrectGuesses / totalGuesses * 100, 1)
                : 0;

            int totalHintsUsed = leaderboard?.Values.Sum(u => u.TotalHintsUsed) ?? 0;

            double avgHintsPerGame = totalGames > 0
                ? Math.Round((double)totalHintsUsed / totalGames, 2)
                : 0;

            int totalPoints = leaderboard?.Values.Sum(u => u.Points) ?? 0;

            string fastestGuessText = "N/A";
            if (leaderboard != null && leaderboard.Values.Any(u => u.FastestCorrectGuess.HasValue))
            {
                var fastestGuessUser = leaderboard
                    .Where(x => x.Value.FastestCorrectGuess.HasValue)
                    .OrderBy(x => x.Value.FastestCorrectGuess.Value)
                    .First();

                var member = await ctx.Guild.GetMemberAsync(fastestGuessUser.Key);

                fastestGuessText =
                    $"{member.Mention} — {fastestGuessUser.Value.FastestCorrectGuess.Value.TotalSeconds:0.00}s";
            }


            // Avg correct guess time (server)
            double totalCorrectSeconds = leaderboard?.Values.Sum(u => u.TotalCorrectGuessTime.TotalSeconds) ?? 0;
            int correctGuessCount = leaderboard?.Values.Sum(u => u.TotalCorrectGuesses) ?? 0;

            string avgCorrectGuessTimeText = correctGuessCount > 0
                ? $"{(totalCorrectSeconds / correctGuessCount):0.00}s"
                : "N/A";

            // Longest streak ever
            string longestStreakText = "N/A";

            if (leaderboard != null && leaderboard.Values.Any(u => u.HighestStreak > 0))
            {
                var longestStreakUser = leaderboard
                    .OrderByDescending(x => x.Value.HighestStreak)
                    .First();

                var member = await ctx.Guild.GetMemberAsync(longestStreakUser.Key);

                longestStreakText =
                    $"{member.Mention} — {longestStreakUser.Value.HighestStreak}";
            }


            // Most played playlist (server-wide)
            var allGames = Program.GameHistory.ContainsKey(ctx.Guild.Id)
                ? Program.GameHistory[ctx.Guild.Id]
                : new List<GameState>();

            var topPlaylistGroup = allGames
                .GroupBy(g => new { g.PlaylistUrl, g.PlaylistName })
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            string topPlaylistText = topPlaylistGroup != null
                ? $"{topPlaylistGroup.Count()} plays — {topPlaylistGroup.Key.PlaylistName} ({topPlaylistGroup.Key.PlaylistUrl})"
                : "N/A";

            // Most guessed artist
            var topArtist = games
                .Where(g => g.ArtistGuessed)
                .GroupBy(g => g.TrackArtist)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            string topArtistText = topArtist != null
                ? $"{topArtist.Key} ({topArtist.Count()})"
                : "N/A";

            // Most guessed song
            var topSong = games
                .Where(g => g.TitleGuessed)
                .GroupBy(g => g.TrackName)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            string topSongText = topSong != null
                ? $"{topSong.Key} ({topSong.Count()})"
                : "N/A";

            var embed = new DiscordEmbedBuilder
            {
                Title = "Server Stats",
                Color = DiscordColor.Blurple
            };

            embed.AddField("Total Games Played", totalGames.ToString(), true);
            embed.AddField("Total Points Earned", totalPoints.ToString(), true);
            embed.AddField("Guess Accuracy", $"{accuracy}%", true);
            embed.AddField("Total Guesses Made", totalGuesses.ToString(), true);
            embed.AddField("Total Hints Used", totalHintsUsed.ToString(), true);
            embed.AddField("Avg Hints/Game", avgHintsPerGame.ToString(), true);
            embed.AddField("Fastest Correct Guess", fastestGuessText, true);
            embed.AddField("Avg Correct Guess Time", avgCorrectGuessTimeText, true);
            embed.AddField("Longest Streak Ever", longestStreakText, true);
            embed.AddField("Most Guessed Artist", topArtistText, true);
            embed.AddField("Most Guessed Song", topSongText, true);
            embed.AddField("Most Played Playlist", topPlaylistText, false);

            await ctx.Channel.SendMessageAsync(embed: embed);
        }

        // Displays the user's current and highest streak
        [Command("streak"), Aliases("str")]
        public async Task Streak(CommandContext ctx)
        {
            if (!Program.Leaderboards.TryGetValue(ctx.Guild.Id, out var board) ||
                !board.TryGetValue(ctx.User.Id, out var score))
            {
                await ctx.RespondAsync("You don’t have any streaks yet.");
                return;
            }

            await ctx.RespondAsync(
                $"Your current streak is **{score.CurrentStreak}**\n" +
                $"Your highest streak is **{score.HighestStreak}**"
            );
        }

        // Displays the streak leaderboard
        [Command("streakleaderboard"), Aliases("strlb")]
        public async Task StreakLeaderboard(CommandContext ctx)
        {
            if (!Program.Leaderboards.TryGetValue(ctx.Guild.Id, out var board) || board.Count == 0)
            {
                await ctx.RespondAsync("No streak data yet.");
                return;
            }

            var embed = new DiscordEmbedBuilder
            {
                Title = "Streaks Leaderboard",
                Color = DiscordColor.Gold
            };

            // Get users with highest streaks
            var entries = board.Where(x => x.Value.HighestStreak > 0)
                .OrderByDescending(x => x.Value.HighestStreak)
                .Select(async x =>
                {
                    var user = await ctx.Guild.GetMemberAsync(x.Key);
                    return (Mention: user.Mention, Streak: x.Value.HighestStreak);
                })
                .Select(t => t.Result).ToList();



            int maxLength = entries.Max(e => e.Mention.Length);

            var lines = entries
                .Select((e, index) =>
                    $"{index + 1}. {e.Mention.PadRight(maxLength)} — {e.Streak} streak"
                );

            embed.Description = string.Join("\n", lines);

            await ctx.Channel.SendMessageAsync(embed: embed);
        }


        // Provides a hint to the user
        [Command("hint"), Aliases("h")]
        public async Task Hint(CommandContext ctx)
        {

            if (!ActiveGames.TryGetValue(ctx.Channel.Id, out var game))
            {
                await ctx.RespondAsync("No active game.");
                return;
            }

            EnsureScore(ctx.Guild.Id, ctx.User.Id);

            // Check if there are any hints left
            if (game.HintsUsed >= game.HintOrder.Count)
            {
                await ctx.RespondAsync("No more hints available.");
                return;
            }

            // Check if user has enough points
            if (Program.Leaderboards[ctx.Guild.Id][ctx.User.Id].Points <= 0)
            {
                await ctx.RespondAsync("You need at least 1 point to buy a hint.");
                return;
            }

            var hintType = game.HintOrder[game.HintsUsed];

            // Load extra data if needed
            if (!game.ExtraDataLoaded &&
            (hintType == HintType.Popularity || hintType == HintType.Genres))
            {
                var info = await _spotify.LoadExtraInfo(game.TrackName, game.TrackArtist);
                game.TrackPopularity = info.trackPop;
                game.ArtistPopularity = info.artistPop;
                game.ArtistGenres = info.genres;
                game.ExtraDataLoaded = true;
            }

            game.HintsUsed++;


            var score = Program.Leaderboards[ctx.Guild.Id][ctx.User.Id];
            score.Points -= 1;

            score.TotalHintsUsed++;

            LeaderboardService.Save(Program.Leaderboards);

            string hint = GetHintText(game, hintType);

            await ctx.Channel.SendMessageAsync($"{hint}");



        }


        // Displays help information
        [Command("help")]
        public async Task Help(CommandContext ctx)
        {
            var embed = new DiscordEmbedBuilder
            {
                Title = "Musicle Commands:",
                Color = DiscordColor.Gray,
            };

            embed.AddField(";play (spotify playlist url)", "Starts a new round (keeps going till you ;break), if you don't paste a url it'll use a default playlist instead");
            embed.AddField(";pass / ;p", "Skips to the next snippet of the current track.");
            embed.AddField(";guess <your guess> / ;g <your guess>", "Guess the title or artist of the current track.");
            embed.AddField(";giveup / ;gu", "Ends the current round and reveals the track.");
            embed.AddField(";hint / ;h", "Gives you a hint about the current snippet (costs 1 point).");
            embed.AddField(";break / ;b", "Stops the bot automatically starting new rounds.");
            embed.AddField(";setplaylist / ;sp", "Sets a default playlist for just you, for when you do ;play");
            embed.AddField(";removeplaylist / ;rp", "Removes your default playlist");
            embed.AddField(";myplaylist / ;mp", "Shows your current default playlist");
            embed.AddField(";streak / ;str", "Shows your current and best streak.");
            embed.AddField(";stats", "Shows your stats.");
            embed.AddField(";serverstats / ;sstats", "Shows the servers stats as a collective.");
            embed.AddField(";leaderboard / ;lb", "Shows the server leaderboard with points.\nScoring:\n1s = +3 \n3s = +2 \n 5s+ = +1\n +1 bonus point if u get artist and title");
            embed.AddField(";streakleaderboard / ;strlb", "Shows a leaderboard ordered by best streak of all time.");
            embed.AddField(";help", "Shows this help message.");
            embed.AddField("EXCLAIMER:", "Becuase the bot pulls the songs from youtube, sometimes it will play music videos.\n\n" +
                "The bot currently only works when i run it myself, so if its offline im sorry!\n\n" +
                "Current default playlist: Top 500 Most Streamed songs on Spotify https://open.spotify.com/playlist/51cZLTqi8xgNWTH0AkIqVU");

            await ctx.Channel.SendMessageAsync(embed: embed);
        }

        // sends an audio snippet to the channel
        private async Task SendSnippet(CommandContext ctx, SpotifyService.TrackData track, int seconds)
        {
            if (!ActiveGames.TryGetValue(ctx.Channel.Id, out var game))
                return;

            // Determines snippet start time
            if (game.SnippetStartSeconds == 0)
            {
                int min = 5;
                int max = Math.Max(min, track.DurationSeconds - seconds - 30);
                game.SnippetStartSeconds = Rng.Next(min, max + 1);
            }

            string oggPath = await AudioService.DownloadSnippetAsOggAsync(
                $"{track.Name} {track.Artist}",
                seconds,
                game.SnippetStartSeconds
            );

            try
            {
                using var fs = new FileStream(
                    oggPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                );
                // send the snippet
                await ctx.Channel.SendMessageAsync(
                    new DiscordMessageBuilder()
                        .WithContent($"**{seconds}s snippet** — guess with `;guess`")
                        .AddFile("snippet.ogg", fs)
                );
                game.RoundStartTime = DateTime.UtcNow;
            }
            //  delete the temp file after a delay
            finally
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    try { File.Delete(oggPath); } catch { }
                });
            }

            // Announce point multiplier for new rounds
            if (game.SnippetIndex == 0 && game.PointMultiplier > 1)
            {
                string msg = game.PointMultiplier == 3
                    ? "**TRIPLE POINT ROUND!**"
                    : "**DOUBLE POINT ROUND!**";

                await ctx.Channel.SendMessageAsync(msg);
            }


            game.TimeoutToken?.Cancel();
            game.TimeoutToken = new CancellationTokenSource();

            _ = HandleRoundTimeout(ctx, game, game.TimeoutToken.Token);

        }

        // handles round timeout
        private async Task HandleRoundTimeout(CommandContext ctx, GameState game, CancellationToken token)
        {
            // wait for 2 minutes or until cancelled
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            game.ConsecutiveTimeouts++;

            // Auto-play check
            if (!ActiveGames.ContainsKey(ctx.Channel.Id) || !game.AutoPlay)
                return;

            // Stop auto-play after 3 consecutive timeouts
            if (game.ConsecutiveTimeouts >= 3)
            {
                game.AutoPlay = false;
                game.ConsecutiveTimeouts = 0;

                await ctx.Channel.SendMessageAsync(
                    "**Auto-play stopped.**"
                );

                return;
            }

            // Reveal answer
            await ctx.Channel.SendMessageAsync(
                $"**Time's up!**\n" +
                $"Title: **{game.TrackName}**\n" +
                $"Artist: **{game.TrackArtist}**"
            );

            if (game.IsEnding)
                return;

            game.IsEnding = true;
            ActiveGames.Remove(ctx.Channel.Id);

            if (!Program.GameHistory.ContainsKey(ctx.Guild.Id))
                Program.GameHistory[ctx.Guild.Id] = new();

            Program.GameHistory[ctx.Guild.Id].Add(game);
            GameHistoryService.Save(Program.GameHistory);


            foreach (var userId in game.PlayersInGame)
            {
                EnsureScore(ctx.Guild.Id, userId);
                var score = Program.Leaderboards[ctx.Guild.Id][userId];

                bool guessedSomethingCorrect =
                    game.TitleGuessedBy == userId ||
                    game.ArtistGuessedBy == userId;

                if (guessedSomethingCorrect)
                    continue;

                int lostStreak = score.CurrentStreak;

                if (lostStreak >= 10)
                {
                    await ctx.Channel.SendMessageAsync(
                        $"💔 <@{userId}> lost a **{lostStreak}** streak!"
                    );
                }

                score.CurrentStreak = 0;
                score.AnnouncedNewBestThisRun = false;
            }

            await HandleStreakLosses(ctx, game, RoundEndReason.Timeout);
            await StartNextRound(ctx, game);
        }




        // ensures user has a score entry
        private void EnsureScore(ulong guildId, ulong userId)
        {
            if (!Program.Leaderboards.ContainsKey(guildId))
                Program.Leaderboards[guildId] = new();

            if (!Program.Leaderboards[guildId].ContainsKey(userId))
                Program.Leaderboards[guildId][userId] = new UserScore();
        }

        // checks if input is a close match to target based on threshold
        private static bool IsCloseMatch(string input, string target, double threshold)
        {
            string a = Normalize(input);
            string b = Normalize(target);

            if (a.Contains(b))
                return true;

            return Similarity(a, b) >= threshold;
        }

        // normalizes strings for comparison
        private static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.ToLowerInvariant();

            // Remove (...) and [...]
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"[\(\[].*?[\)\]]",
                ""
            );

            // Remove common Spotify junk words
            string[] junk =
            {
        "feat",
        "featuring",
        "with",
        "remaster",
        "remastered",
        "radio edit",
        "edit",
        "mono",
        "stereo",
        "version",
        "mix",
        "deluxe",
        "bonus track",
        "remastered",
        "1960", "1961", "1962", "1963", "1964", "1965", "1966", "1967", "1968", "1969", "1970", "1971", "1972", "1973", "1974", "1975", "1976", "1977", "1978",
        "1979", "1980", "1981", "1982", "1983", "1984", "1985", "1986", "1987", "1988", "1989", "1990", "1991", "1992", "1993", "1994", "1995", "1996","1997",
        "1998", "1999", "2000", "2001", "2002", "2003", "2004", "2005", "2006", "2007", "2008", "2009", "2010", "2011", "2012", "2013", "2014","2015", "2016",
        "2017", "2018", "2019", "2020", "2021", "2022", "2023", "2024", "2025", "2026",
        "from",
        "original",
        "original version",
        "album version",
        "single version",
        "extended",
        "extended mix",
        "full version",
        "live",
        "live version",
        "live at",
        "acoustic",
        "unplugged",
        "session",
        "explicit",
        "clean",
        "censored",
        "official",
        "official audio",
        "official video",
        "lyric video",
        "lyrics",
        "audio",
        "video",
        "ft",
        "ft.",
        "feat.",
        "x",
        "remaster edition",
        "anniversary edition",
        "special edition",
        "expanded edition",
        "soundtrack",
        "ost",
        "theme",
        "score",
        "radio"

    };
            // Remove junk words from string
            foreach (var word in junk)
                text = text.Replace(word, "");

            // Remove diacritics and non-letter/digit characters
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);

                if (uc != UnicodeCategory.NonSpacingMark &&
                    (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)))
                {
                    sb.Append(c);
                }
            }

            // Collapse multiple spaces
            string result = System.Text.RegularExpressions.Regex
                .Replace(sb.ToString(), @"\s+", " ")
                .Trim();

            return result;
        }

        // calculates similarity between guess and title/artist
        private static double Similarity(string s1, string s2)
        {
            int distance = Levenshtein(s1, s2);
            int maxLen = Math.Max(s1.Length, s2.Length);
            return maxLen == 0 ? 1.0 : 1.0 - (double)distance / maxLen;
        }

        // Levenshtein distance algorithm
        private static int Levenshtein(string s, string t)
        {
            int[,] d = new int[s.Length + 1, t.Length + 1];

            for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= t.Length; j++) d[0, j] = j;

            for (int i = 1; i <= s.Length; i++)
            {
                for (int j = 1; j <= t.Length; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost
                    );
                }
            }

            return d[s.Length, t.Length];
        }

        // sets points based on snippet duration
        private int GetPointsForSnippet(int seconds)
        {
            if (seconds == 1) return 3;
            if (seconds == 3) return 2;
            return 1;
        }

        // randomizes point multiplier for the round (8% chance for 2x, 2% chance for 3x)
        private int RollPointMultiplier()
        {
            int roll = Rng.Next(100);
            if (roll < 2) return 3;
            if (roll < 10) return 2;
            return 1;
        }


        // starts the next round in auto-play mode
        private async Task StartNextRound(CommandContext ctx, GameState previousGame)
        {
            if (!previousGame.AutoPlay)
                return;

            var tracks = await _spotify.GetTracksFromPlaylist(previousGame.PlaylistUrl);
            if (tracks.Count == 0)
                return;

            var playlistName = previousGame.PlaylistName ?? await _spotify.GetPlaylistName(previousGame.PlaylistUrl);

            SpotifyService.TrackData track;

            do
            {
                track = tracks[Rng.Next(tracks.Count)];
            }
            while (
                previousGame.LastTrackKey ==
                $"{track.Name}|{track.Artist}"
                && tracks.Count > 1
            );


            var newGame = new GameState
            {
                TrackName = track.Name,
                TrackArtist = track.Artist,
                TrackYear = track.Year,
                TrackDurationSeconds = track.DurationSeconds,
                SnippetIndex = 0,
                PlaylistUrl = previousGame.PlaylistUrl,
                PlaylistName = playlistName,
                AutoPlay = true,
                LastTrackKey = $"{track.Name}|{track.Artist}",
                ArtistGenres = track.ArtistGenres,
                TrackPopularity = track.TrackPopularity,
                ArtistPopularity = track.ArtistPopularity,
                AlbumName = track.AlbumName,
                ConsecutiveTimeouts = previousGame.ConsecutiveTimeouts,
            };

            newGame.PointMultiplier = RollPointMultiplier();

            newGame.HintOrder = Enum.GetValues<HintType>()
            .OrderBy(_ => Rng.Next())
            .ToList();
            newGame.HintsUsed = 0;

            ActiveGames[ctx.Channel.Id] = newGame;


            await Task.Delay(500);
            await SendSnippet(ctx, track, GameState.SnippetDurations[0]);

        }


        // helper for hint
        private string RevealRandomLetters(string text)
        {
            if (text.Length <= 2)
                return text;

            var chars = text.ToCharArray();
            var revealed = new HashSet<int>();

            int revealCount = Math.Max(1, text.Length / 4);

            while (revealed.Count < revealCount)
                revealed.Add(Rng.Next(text.Length));

            for (int i = 0; i < chars.Length; i++)
            {
                if (!revealed.Contains(i) && char.IsLetter(chars[i]))
                    chars[i] = '_';
            }

            return new string(chars);
        }

        // helper for hint
        private string Scramble(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return string.Join(" ",
                text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(word =>
                        new string(word.OrderBy(_ => Rng.Next()).ToArray())
                    )
            );
        }

        // helper for hint
        private int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return text
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Length;
        }

        // helper for hint
        private int CountLettersOnly(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return text.Count(char.IsLetter);
        }



        private string GetHintText(GameState game, HintType type)
        {
            string title = game.TrackName;
            string artist = game.TrackArtist;


            // Generate hint based on type
            return type switch
            {


                HintType.Year =>
                    game.TrackYear > 0
                         ? $"The song was released in **{game.TrackYear}**."
                         : "Release year not available.",


                HintType.FirstLetter =>
                    $"First letter of the title is: **{title[0]}**\nFirst letter of the artist is: **{artist[0]}**",

                HintType.Length =>
                    $"Title length: **{CountLettersOnly(title)}** letters\n" +
                    $"Artist length: **{CountLettersOnly(artist)}** letters",

                HintType.RandomLetters =>
                    $"Title: `{RevealRandomLetters(title)}`\nArtist: `{RevealRandomLetters(artist)}`",

                HintType.Scrambled =>
                    $"Scrambled Title: **{Scramble(title)}** \nScrambled Artist: **{Scramble(artist)}**",

                HintType.Words =>
                    $"Amount of words in title: **{CountWords(title)}**\nAmount of words in artist: **{CountWords(artist)}**",

                HintType.Popularity =>
                    $"Track popularity: **{game.TrackPopularity}/100**\n" +
                    $"Artist popularity: **{game.ArtistPopularity}/100**",

                HintType.Genres =>
                    game.ArtistGenres.Count > 0
                        ? $"Artist genre(s): **{string.Join(", ", game.ArtistGenres.Take(3))}**"
                        : "No genre information available.",

                HintType.Album =>
                    string.IsNullOrWhiteSpace(game.AlbumName)
                        ? "Album information not available."
                    : Normalize(game.AlbumName) == Normalize(game.TrackName)
                        ? "Album name is the same as the song title."
                        : $"The song is from the album **{game.AlbumName}**.",

                HintType.RevealVowels =>
                    $"Title: `{string.Concat(title.Select(c => "aeiouAEIOU".Contains(c) ? c : '_'))}`\n" +
                    $"Artist: `{string.Concat(artist.Select(c => "aeiouAEIOU".Contains(c) ? c : '_'))}`",

                HintType.EveryOtherLetter =>
                    $"Title: `{string.Concat(title.Select((c, i) => i % 2 == 0 ? c : '_'))}`\n" +
                    $"Artist: `{string.Concat(artist.Select((c, i) => i % 2 == 0 ? c : '_'))}`",

                HintType.WordCountPattern =>
                    $"Pattern — Title: {string.Join(", ", title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length))} letters,\n" +
                    $"Artist: {string.Join(", ", artist.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length))} letters",



                _ => "Unkown hint."


            };


        }


        // Streak milestones and their corresponding bonus points
        private static readonly Dictionary<int, int> StreakMilestones = new()
        {
            {10, 5 },
            {20, 5 },
            {30, 10 },
            {40, 10 },
            {50, 15 },
            {60, 15 },
            {70, 20},
            {80, 20 },
            {90, 25 },
            {100, 25 },
            {125, 30 },
            {150, 30 },
            {175, 40 },
            {200, 40 },
            {225, 50 },
            {250, 50 },
            {275, 60 },
            {300, 75 }
        };

        private async Task HandleCorrectGuess(CommandContext ctx, ulong userId, UserScore score, GameState game)
        {
            score.CurrentStreak++;

            // keeps highest streak in sync
            if (score.CurrentStreak > score.HighestStreak)
            {
                score.HighestStreak = score.CurrentStreak;

                // announce new personal best only once per run
                if (!score.AnnouncedNewBestThisRun)
                {
                    score.AnnouncedNewBestThisRun = true;

                    var member = await ctx.Guild.GetMemberAsync(userId);
                    await ctx.Channel.SendMessageAsync(
                        $"**{member.DisplayName}** beat their personal best with a streak of **{score.HighestStreak}**!"
                    );
                }
            }

            // Milestone bonus + message
            if (StreakMilestones.TryGetValue(score.CurrentStreak, out int bonus))
            {
                score.Points += bonus;
                score.TotalBonusPoints += bonus;

                var member = await ctx.Guild.GetMemberAsync(userId);
                await ctx.Channel.SendMessageAsync(
                    $"🎉 **{member.DisplayName}** reached a **{score.CurrentStreak}** correct-guess streak! (+{bonus})"
                );
            }

            var guessTime = DateTime.UtcNow - game.RoundStartTime;

            // count correct guesses
            score.TotalCorrectGuesses++;

            // add to total time
            score.TotalCorrectGuessTime += guessTime;

            // fastest guess check/update
            if (!score.FastestCorrectGuess.HasValue || guessTime < score.FastestCorrectGuess)
            {
                score.FastestCorrectGuess = guessTime;
            }


            LeaderboardService.Save(Program.Leaderboards);
        }


        private async Task HandleStreakLosses(
                    CommandContext ctx,
                    GameState game,
                    RoundEndReason reason
)
        {
            foreach (var userId in game.PlayersInGame)
            {
                EnsureScore(ctx.Guild.Id, userId);
                var score = Program.Leaderboards[ctx.Guild.Id][userId];

                bool guessedCorrect = game.UsersHasGuessedRight.Contains(userId);
                bool guessedWrong = game.UsersWithIncorrectGuess.Contains(userId);
                bool guessedAnything = guessedCorrect || guessedWrong;

                bool loseStreak = reason switch
                {
                    // ;gu = lose streak unless they guessed title OR artist
                    RoundEndReason.GiveUp =>
                        !guessedCorrect,

                    // ;break = only lose if they guessed AND all guesses were wrong
                    RoundEndReason.Break =>
                        guessedWrong && !guessedCorrect,

                    // timeout = lose unless they didnt guess or they guessed title OR artist
                    RoundEndReason.Timeout =>
                        !guessedCorrect,

                    // someone else solved = only punish wrong guessers
                    RoundEndReason.Solved =>
                        guessedWrong && !guessedCorrect,

                    _ => false
                };

                if (!loseStreak)
                    continue;

                int lostStreak = score.CurrentStreak;

                // if streak lost was 10 or more, announce it
                if (lostStreak >= 10)
                {
                    var member = await ctx.Guild.GetMemberAsync(userId);
                    await ctx.Channel.SendMessageAsync(
                        $"💔 **{member.DisplayName}** lost a **{lostStreak}** streak!"
                    );
                }

                // reset streak
                score.CurrentStreak = 0;
                score.AnnouncedNewBestThisRun = false;
            }
        }



    }
}
