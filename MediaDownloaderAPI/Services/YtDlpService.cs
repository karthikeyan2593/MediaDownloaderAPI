using MediaDownloaderAPI.Models;
using System.Diagnostics;
using System.Text.Json;

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

        // 1. MP3 டவுன்லோட் மெத்தட்
        public async Task<(int exitCode, string output)> DownloadMp3Async(string url, string format)
        {
            string args = $"-x --audio-format {format} \"{url}\"";
            return await RunYtDlpAsync(args);
        }

        // 2. வீடியோ தகவல் எடுக்கும் மெத்தட்
        public async Task<DownloadResponse> GetVideoInfoAsync(string url)
        {
            try
            {
                var (exitCode, output) = await RunYtDlpAsync($"--dump-json --no-playlist \"{url}\"");
                if (exitCode != 0)
                    return new DownloadResponse { Success = false, Error = output };

                var json = JsonDocument.Parse(output);
                var root = json.RootElement;

                var formats = new List<string>();
                var formatsWithDetails = new List<dynamic>();

                if (root.TryGetProperty("formats", out var fmts))
                {
                    var addedHeights = new HashSet<int>();
                    var standardHeights = new HashSet<int> { 144, 240, 360, 480, 720, 1080, 1440, 2160, 4320 };

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

                                long bytes = 0;
                                if (f.TryGetProperty("filesize", out var fs) && fs.ValueKind == JsonValueKind.Number)
                                    bytes = fs.GetInt64();
                                else if (f.TryGetProperty("filesize_approx", out var fsa) && fsa.ValueKind == JsonValueKind.Number)
                                    bytes = fsa.GetInt64();

                                string sizeStr = bytes > 0 ? (bytes / 1048576.0).ToString("0.00") + " MB" : "Unknown";
                                string ext = f.TryGetProperty("ext", out var e) ? e.GetString() : "mp4";

                                formatsWithDetails.Add(new { quality = $"{stdHeight}p", ext = ext, fileSize = sizeStr, url = f.TryGetProperty("url", out var u) ? u.GetString() : "#" });
                            }
                        }
                    }
                    formats = formats.OrderByDescending(f => int.Parse(f.Replace("p", ""))).ToList();
                    formatsWithDetails = formatsWithDetails.OrderByDescending(f => int.Parse(((string)f.quality).Replace("p", ""))).ToList();
                }

                return new DownloadResponse
                {
                    Success = true,
                    Title = root.GetProperty("title").GetString(),
                    Thumbnail = root.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString() : null,
                    AvailableQualities = formats,
                    Formats = formatsWithDetails
                };
            }
            catch (Exception ex)
            {
                return new DownloadResponse { Success = false, Error = ex.Message };
            }
        }

        // 3.yt-dlp ரன் செய்யும் முக்கிய மெத்தட்
        public async Task<(int exitCode, string output)> RunYtDlpAsync(string arguments)
        {
            string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            string finalArguments = $"{arguments} --user-agent \"{userAgent}\" --force-ipv4 --no-check-certificate --no-warnings";

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

        // 4. தேவைப்பட்டால் RapidAPI-ஐ அழைக்க மெத்தட்
        public async Task<string> DownloadViaRapidApiAsync(string url)
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://social-download-all-in-one.p.rapidapi.com/v1/social/autolink");
            request.Headers.Add("x-rapidapi-key", "634d66a1fbmsh348e46cbbe59b16p1531e3jsnea49dffe631");
            request.Headers.Add("x-rapidapi-host", "social-download-all-in-one.p.rapidapi.com");
            request.Content = new StringContent($"{{\"url\": \"{url}\"}}", null, "application/json");

            var response = await client.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }
    }
}