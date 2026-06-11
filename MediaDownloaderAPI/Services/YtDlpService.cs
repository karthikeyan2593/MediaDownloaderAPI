using System.Diagnostics;
using MediaDownloaderAPI.Models;

namespace MediaDownloaderAPI.Services
{
    public class YtDlpService
    {
        private readonly string _ffmpegPath = @"C:\ffmpeg\bin\ffmpeg.exe"; 
        private readonly string _downloadFolder;

        public string FfmpegPath => _ffmpegPath;

        public YtDlpService()
        {
            _downloadFolder = Path.Combine(Path.GetTempPath(), "MediaDownloader");
            if (!Directory.Exists(_downloadFolder))
            {
                Directory.CreateDirectory(_downloadFolder);
            }
        }

        public async Task<DownloadResponse> GetVideoInfoAsync(string url)
        {
            try
            {
                var info = await RunYtDlpAsync($"--dump-json --no-playlist \"{url}\"");
                if (info.exitCode != 0)
                    return new DownloadResponse { Success = false, Error = info.output };

                var json = System.Text.Json.JsonDocument.Parse(info.output);
                var root = json.RootElement;

                var formats = new List<string>();
                if (root.TryGetProperty("formats", out var fmts))
                {
                    var heights = new HashSet<int>();
                    foreach (var f in fmts.EnumerateArray())
                    {
                        if (f.TryGetProperty("height", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.Number)
                        {
                            heights.Add(h.GetInt32());
                        }
                    }

                    var standardHeights = new HashSet<int> { 144, 240, 360, 480, 720, 1080, 1440, 2160, 4320 };
                    formats = heights
                        .Where(h => h >= 144)
                        .Select(h => standardHeights.OrderBy(s => Math.Abs(s - h)).First())
                        .Distinct()
                        .OrderByDescending(h => h)
                        .Select(h => $"{h}p")
                        .ToList();
                }

                return new DownloadResponse
                {
                    Success = true,
                    Title = root.GetProperty("title").GetString(),
                    Thumbnail = root.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString() : null,
                    AvailableQualities = formats
                };
            }
            catch (Exception ex)
            {
                return new DownloadResponse { Success = false, Error = ex.Message };
            }
        }

        public async Task<string?> DownloadMp3Async(string url, string title)
        {
            var outputTemplate = Path.Combine(_downloadFolder, "%(title)s.%(ext)s");
            var args = $"--no-playlist -x --audio-format mp3 --audio-quality 0 --ffmpeg-location \"{_ffmpegPath}\" -o \"{outputTemplate}\" \"{url}\"";

            var info = await RunYtDlpAsync(args);
            if (info.exitCode != 0) return null;

            var file = new DirectoryInfo(_downloadFolder)
                .GetFiles("*.mp3")
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            return file?.FullName;
        }

        private async Task<(int exitCode, string output)> RunYtDlpAsync(string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, string.IsNullOrEmpty(output) ? error : output);
        }
    }
}