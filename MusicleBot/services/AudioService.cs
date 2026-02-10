using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MusicleBot.config;

namespace MusicleBot.services
{
    public static class AudioService
    {

        // Downloads a snippet of a song from YouTube as an OGG file
        public static async Task<string> DownloadSnippetAsOggAsync(
            string searchQuery,
            int durationSeconds,
            int startSeconds
        )
        {
            // Temp directory
            string tempDir = Path.Combine(Path.GetTempPath(), "musicle");
            Directory.CreateDirectory(tempDir);

            // File paths
            string rawPath = Path.Combine(tempDir, $"{Guid.NewGuid()}.opus");
            string oggPath = rawPath.Replace(".opus", ".ogg");

            // Paths to tools
            string ytDlpPath = string.IsNullOrWhiteSpace(Program.Config.ytdlpPath)
                ? "yt-dlp"
                : Program.Config.ytdlpPath; // yt-dlp.exe file location
            string ffmpegPath = string.IsNullOrWhiteSpace(Program.Config.ffmpegPath)
                ? "ffmpeg"
                : Program.Config.ffmpegPath; //ffmpeg.exe file location

            // yt-dlp downloads the best audio and extracts it as opus
            var ytProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments =
                        "--user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" " +
                        "--extractor-args \"youtube:player_client=android\" " +
                        "-x --audio-format opus --audio-quality 0 " +
                        "--no-playlist " +
                        $"-o \"{rawPath}\" " +
                        $"\"ytsearch1:{searchQuery}\"",
                    WorkingDirectory = tempDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            ytProcess.Start();
            await ytProcess.WaitForExitAsync();

            if (!File.Exists(rawPath))
            {
                string error = await ytProcess.StandardError.ReadToEndAsync();
                throw new Exception($"yt-dlp failed to download audio:\n{error}");
            }

            // ffmpeg gets song snippet and converts to ogg then deletes raw file
            var ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments =
                        $"-y -ss {startSeconds} -t {durationSeconds} " +
                        $"-i \"{rawPath}\" " +
                        $"-c:a libopus \"{oggPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            ffmpegProcess.Start();
            await ffmpegProcess.WaitForExitAsync();

            SafeDelete(rawPath);

            if (!File.Exists(oggPath))
                throw new Exception("FFmpeg failed to create snippet");

            return oggPath;
        }

        // Safely delete a file
        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
    }
}
