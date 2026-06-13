using System.Diagnostics;
using System.Text.Json; // இதை சேர்த்திருக்கேன்
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

                var json = JsonDocument.Parse(info.output);
                var root = json.RootElement;

                var formats = new List<string>();
                var formatsWithDetails = new List<object>(); // UI டேபிளுக்காக புது லிஸ்ட்!

                if (root.TryGetProperty("formats", out var fmts))
                {
                    var addedHeights = new HashSet<int>();
                    var standardHeights = new HashSet<int> { 144, 240, 360, 480, 720, 1080, 1440, 2160, 4320 };

                    // ரிவர்ஸ்ல லூப் பண்ணாதான் நல்ல குவாலிட்டி ஃபைல்ஸ் கிடைக்கும்
                    foreach (var f in fmts.EnumerateArray().Reverse())
                    {
                        if (f.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number)
                        {
                            int height = h.GetInt32();
                            if (height < 144) continue;

                            int stdHeight = standardHeights.OrderBy(s => Math.Abs(s - height)).First();

                            if (!addedHeights.Contains(stdHeight))
                            {
                                addedHeights.Add(stdHeight);
                                formats.Add($"{stdHeight}p");

                                // File Size எடுக்கும் பகுதி
                                long bytes = 0;
                                if (f.TryGetProperty("filesize", out var fs) && fs.ValueKind == JsonValueKind.Number)
                                    bytes = fs.GetInt64();
                                else if (f.TryGetProperty("filesize_approx", out var fsa) && fsa.ValueKind == JsonValueKind.Number)
                                    bytes = fsa.GetInt64();

                                string sizeStr = bytes > 0 ? (bytes / 1048576.0).ToString("0.00") + " MB" : "Unknown";
                                string ext = f.TryGetProperty("ext", out var e) ? e.GetString() : "mp4";

                                formatsWithDetails.Add(new
                                {
                                    quality = $"{stdHeight}p",
                                    ext = ext,
                                    fileSize = sizeStr,
                                    url = f.TryGetProperty("url", out var u) ? u.GetString() : "#"
                                });
                            }
                        }
                    }

                    // குவாலிட்டி அடிப்படையில் வரிசைப்படுத்துதல் (1080p, 720p...)
                    formats = formats.OrderByDescending(f => int.Parse(f.Replace("p", ""))).ToList();
                    formatsWithDetails = formatsWithDetails
                        .OrderByDescending(f => int.Parse(((string)((dynamic)f).quality).Replace("p", "")))
                        .ToList();
                }

                return new DownloadResponse
                {
                    Success = true,
                    Title = root.GetProperty("title").GetString(),
                    Thumbnail = root.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString() : null,
                    AvailableQualities = formats,
                    Formats = formatsWithDetails // இந்த புது டேட்டாவை Frontend-க்கு அனுப்புறோம்!
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

            // 1. ப்ராஜெக்ட் ரன் ஆகும் மெயின் போல்டர் பாதையைக் கண்டறிகிறோம்
            string rootPath = AppDomain.CurrentDomain.BaseDirectory;
            // "youtube-cookies.txt" என்பதற்குப் பதிலாக "cookies.txt" என்று மாற்றவும்
            string cookiesPath = Path.Combine(rootPath, "cookies.txt");

            // 2. கமாண்டில் குக்கீஸ் ஃபைலின் முழுப் பாதையையும் இணைக்கிறோம்
            string finalArguments = $"{arguments} --cookies \"{cookiesPath}\" --no-check-certificate --no-warnings";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = finalArguments,
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