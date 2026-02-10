using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MusicleBot.services
{
    public static class AudioService
    {
        private const int MaxAttempts = 2;

        // Returns path to ogg file, or null if it fails after retries
        public static async Task<string?> DownloadSnippetAsOggAsync(
            string searchQuery,
            int durationSeconds,
            int startSeconds
        )
        {
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    return await TryDownloadOnce(
                        searchQuery,
                        durationSeconds,
                        startSeconds
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[AudioService] Attempt {attempt} failed: {ex.Message}"
                    );

                    if (attempt == MaxAttempts)
                    {
                        Console.WriteLine(
                            "[AudioService] Skipping song after repeated failure."
                        );
                        return null;
                    }
                }
            }

            return null;
        }

        private static async Task<string> TryDownloadOnce(
            string searchQuery,
            int durationSeconds,
            int startSeconds
        )
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "musicle");
            Directory.CreateDirectory(tempDir);

            string rawPath = Path.Combine(tempDir, $"{Guid.NewGuid()}.opus");
            string oggPath = rawPath.Replace(".opus", ".ogg");

            string ytDlpPath = string.IsNullOrWhiteSpace(Program.Config.ytdlpPath)
                ? "yt-dlp"
                : Program.Config.ytdlpPath;

            string ffmpegPath = string.IsNullOrWhiteSpace(Program.Config.ffmpegPath)
                ? "ffmpeg"
                : Program.Config.ffmpegPath;

            
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
                throw new Exception($"yt-dlp failed: {error}");
            }

            
            var ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments =
                        $"-y -ss {startSeconds} -t {durationSeconds} " +
                        $"-i \"{rawPath}\" " +
                        $"-c:a libopus \"{oggPath}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            ffmpegProcess.Start();
            await ffmpegProcess.WaitForExitAsync();

            SafeDelete(rawPath);

            if (!File.Exists(oggPath))
                throw new Exception("ffmpeg failed to create ogg snippet");

            return oggPath;
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // intentionally ignored
            }
        }
    }
}
