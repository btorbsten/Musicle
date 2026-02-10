using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;
using MusicleBot.commands;
using MusicleBot.config;
using MusicleBot.other;
using MusicleBot.services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MusicleBot
{
    internal class Program
    {
        public static JSONReader Config;
        public static Dictionary<ulong, Dictionary<ulong, UserScore>> Leaderboards;
        public static Dictionary<ulong, string> UserDefaultPlaylist = new();
        public static Dictionary<ulong, List<GameState>> GameHistory = new();

        static async Task Main()
        {





            Config = new JSONReader();
            await Config.ReadJSON();




            Leaderboards = LeaderboardService.Load();
            UserDefaultPlaylist = DefaultPlaylistService.Load();
            GameHistory = GameHistoryService.Load();


            var discord = new DiscordClient(new DiscordConfiguration
            {
                Token = Config.token,
                TokenType = TokenType.Bot,
                Intents = DiscordIntents.All
            });




            var services = new ServiceCollection()
                .AddSingleton(new SpotifyService(
                    Config.spotifyClientId,
                    Config.spotifyClientSecret))
                .BuildServiceProvider();


            var commands = discord.UseCommandsNext(new CommandsNextConfiguration
            {
                StringPrefixes = new[] { Config.prefix },
                Services = services,
                EnableMentionPrefix = true,
                EnableDms = false,
                EnableDefaultHelp = false
            });


            commands.RegisterCommands<MusicCommands>();


            discord.Ready += async (client, e) =>
            {
                await client.UpdateStatusAsync(
                    new DiscordActivity(";help", ActivityType.ListeningTo)
                );
            };
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(60000);
                    LeaderboardService.Save(Leaderboards);
                    DefaultPlaylistService.Save(UserDefaultPlaylist);
                    GameHistoryService.Save(GameHistory);
                }
            });


            await discord.ConnectAsync();

            await Task.Delay(-1);

        }
    }
}
