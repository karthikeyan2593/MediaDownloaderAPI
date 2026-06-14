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
        public async Task<(int exitCode, string output)> DownloadMp3Async(string url, string format)
        {
            // இங்க நீ ரெண்டு Argument வாங்குற மாதிரி மாத்தியிருக்கோம் (url, format)
            string args = $"-x --audio-format {format} \"{url}\"";
            return await RunYtDlpAsync(args);
        }
        public async Task<DownloadResponse> GetVideoInfoAsync(string url)
        {
            try
            {
                // இப்போ RunYtDlpAsync குடுக்குற Tuple (exitCode, output) இங்க கரெக்டா மேட்ச் ஆகிடும்!
                var (exitCode, output) = await RunYtDlpAsync($"--dump-json --no-playlist \"{url}\"");
                if (exitCode != 0)
                    return new DownloadResponse { Success = false, Error = output };

                var json = JsonDocument.Parse(output);
                var root = json.RootElement;

                var formats = new List<string>();
                var formatsWithDetails = new List<dynamic>(); // dynamic ஆக மாற்றப்பட்டுள்ளது

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

                    formats = formats.OrderByDescending(f => int.Parse(f.Replace("p", ""))).ToList();

                    // க்ளீனா குவாலிட்டி படி ஆர்டர் செய்ய லாஜிக் திருத்தப்பட்டுள்ளது
                    formatsWithDetails = formatsWithDetails
                        .OrderByDescending(f => int.Parse(((string)f.quality).Replace("p", "")))
                        .ToList();
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

        // உன்னுடைய Controller மற்றும் GetVideoInfoAsync இரண்டுக்கும் செட் ஆகுற மாதிரி Tuple ரிட்டன் டைப் மாற்றப்பட்டுள்ளது!
        public async Task<(int exitCode, string output)> RunYtDlpAsync(string arguments)
        {
            // 1. உன் குக்கீஸ் டேட்டாவை ஃபார்மட் உடையாத மாதிரி Base64 கோடா மாத்தி வச்சிருக்கேன்
            string base64Cookies = "IyBOZXRzY2FwZSBIVFRQIENvb2tpZSBGaWxlCiMgaHR0cHM6Ly9jdXJsLmhheHguc2UvcmZjL2Nvb2tpZV9zcGVjLmh0bWwKIyBUaGlzIGlzIGEgZ2VuZXJhdGVkIGZpbGUhIERvIG5vdCBlZGl0LgouaW5zdGFncmFtLmNvbQlUUlVFCS8JVFJVRQkxODEzODI0NzY4CWRhdHIKQW0wTmFyQ2J0ck8za004RW9WMHlVZWJrCi5pbnN0YWdyYW0uY29tCVRSVUUvCVRSVUUJMzgxMDgwMDc2OAlpZ19kaWQJQzdEREZFQjEtMjg4NS00MjBCLTk0REQtRjdEOEVDMUY5N0EyCi5pbnN0YWdyYW0uY29tCVRSVUUvCVRSVUUJMzgxMzgyNDc3MAltaWQJYWcxdEFnQUxBQUdoRGNjdEFtdlhRMlVwZjhWVgouaW5zdGFncmFtLmNvbQlUUlVFCS8JVFJVRQkxODEwODAxMDAzCWlnX25yY2IJMwouaW5zdGFncmFtLmNvbQlUUlVFCS8JVFJVRQkxODE1OTMxMTEwCWNzcmZ0b2tlbgNPcnFOeWtEM0diMXBKaUJqU3lnMkNNNXZKak4wc1lveAouaW5zdGFncmFtLmNvbQlUUlVFCS8JVFJVRQkxNzg5MTQ3MTEwCWRzX3VzZXJfaWQJMzQ1MTgwOTYzOQouaW5zdGFncmFtLmNvbQlUUlVFCS8JVFJVRQkxODE1MDY0NTY4CXBzX2wJMwouaW5zdGFncmFtLmNvbQlUUlVFCS8JVFJVRQkxODE1MDY0NTY4CXBzX24JMwouaW5zdGFncmFtLmNvbQlUUlVFCS8JVFJVRQkxNzgxOTc1OTAxCWRwcgkxLjI1Ci5pbnN0YWdyYW0uY29tCVRSVUUvCVRSVUUJMTCzgMTk3NTkwMQl3ZAkxNTM2eDczMAouaW5zdGFncmFtLmNvbQlUUlVFCS8JVFJVRQkxODEyOTA3MTA4CXNlc3Npb25pZAkzNDUxODA5NjM5JTNBM0F4cXZoVlRUWXRmRjYlM0E1JTNBQVloZm90YTNWMDFWTDZRYXJMdWVJVWQxVkJYYWVjRXVuYmRtS3VNVHcwCi5pbnN0YWdyYW0uY29tCVRSVUUvCVRSVUUJMwlydXIJIkVBR1wwNTQzNDUxODA5NjM5XDA1NDE4MTI5MDcxMDk6MDFmZmU5ZDdjOWYxYWJmNzhhMWYwZjc4N2MyZGQxZTE1NTc5NzZmM2Y4ODY0MjM3YzY4ZjYyNzVmNWViODhiYWE4Y2YxZmZjIgoeX3lvdXR1YmUuY29tCVRSVUUvCVRSVUUJMTE5MTc5NzYyMglfX1NlY3VyZS1CVUNLRVQJQ2F3Ci55b3V0dWJlLmNvbQlUUlVFCS8JVFJVRQkxODE1OTI0OTY2CVBSRUYJZjQ9NDAwMDAwMCZ0ej1Bc2lhLkNhbGN1dHRhJmY3PTEwMCZmNj00MDAwMDAwMAoueW91dHViZS5jb20JVFJVUUvCVRSVUUJMTE4MTI3MjgwODIJX19TZWN1cmUtMVBTSURUUwlzaWR0cy1Dak crappy9qVTB音楽THpMbGdwVW9DSkM5VDVJeGpiRnhDZjR3QmZxamhqbWhqd1NIUDhleC1tejd0WEVUZy1hQTB6T1hvMXJPRUFBCi55b3V0dWJlLmNvbQlUUlVFCS8JVFJVRQkxODEyNzI4MDgyCV9fU2VjdXJlLTNQU0lEVFMJc2lkdHMtQ2pRQnlvalJVMUhDTHpsZ3BVb0NKQzlUNUl4amJFeENmNHdCZnFqaGptaGp3U0hQOGV4LW16N3RYRVRnLWFBMHpPWE8xck9FQUEKLnlvdXR1YmUuY29tCVRSVUUvCVRSVUUJMTE7OTY5MTY5MjcJVklTSVRPUl9JTkZPMS9MSVZFCU1jV3VGSG9OTlVRCi55b3V0dWJlLmNvbQlUUlVFCS8JVFJVRQkxMTc5NjkxNjkyNwlWSVNJVE9SX1BSSVZBQ1lfTUVUQURBVEEJQ2dKSVRoSUVHZ0FnUHclM0QlM0QKLnlvdXR1YmUuY29tCVRSVUUvCVRSVUUJMTE7OTY5MTY5MjQJVl9fU2VjdXJlLVlOSUQJMTkuWVQ9bE9HSllfaW1QTzJrV1pnelBoR24wYlFnWTNTSEtTM2FrNWZaX1RFQ1d1dVY3OWtoVmZPb18yV01yVlpidVVaNnN5TzlLdDRWdUV3YnVlZUJkWWhnd0luaGxMOVF3ZFJaMDhTemF4cEZqUEFfZkgwVUIxUkxkdE15cWhQejlkR1F5Y2ZIRm1Na1NINmxWdjVQTk5OTzJkRklGTU5jTDhCQ0VIaUc0d2Vub2cxSlpRYjlXVEVCM3dSWGVNeVo1YklsQV9wejZTb2ZzSXh6VFlmLUMtR0YwOXdjd2RldUJjWWVNQmpPN3lWMnlUTTlKS0lxZmJnbWRjdDBVM09KblFWZ1llbVpYTDNBbG9FTTZwQTIzNF8weXNMUzBpczZ2MEVXVTVNMXoyd3I0RE43NGp2V05JSHdYaVJUX2lQWVFLcGItZERHVmR2cC1yYnBlM2lJUmhuUjhRCi55b3V0dWJlLmNvbQlUUlVFCS8JVFJVRQkMwlZTQwU1bUgyVXkzTVhQRQoueW91dHViZS5jb20JVFJVUUvCVRSVUUJMTE7OTY5MTY5MjQJVl9fU2VjdXJlLVJPTExPVVRfVE9LRU4JQ0xUQ3Q0U1BwYWVzMmdFUWpZYkUwc2J2a3dNWTB0UDAuOFdFbFFNJTNEMw==";

            byte[] cookieBytes = Convert.FromBase64String(base64Cookies);
            string myCookiesContent = System.Text.Encoding.UTF8.GetString(cookieBytes);

            string tempCookiesPath = Path.Combine(Path.GetTempPath(), "runtime-cookies.txt");
            await File.WriteAllTextAsync(tempCookiesPath, myCookiesContent);

            string finalArguments = $"{arguments} --cookies \"{tempCookiesPath}\" --no-check-certificate --no-warnings";

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

            string finalResult = string.IsNullOrEmpty(output) ? error : output;
            return (process.ExitCode, finalResult);
        }
    }
}