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

            // 1. உன் குக்கீஸ் டேட்டாவை அப்படியே ஸ்ட்ரிங்கா கோட்டுக்குள்ளேயே கொண்டு வரோம்
            string myCookiesContent = @"# Netscape HTTP Cookie File
# https://curl.haxx.se/rfc/cookie_spec.html
# This is a generated file! Do not edit.

.instagram.com	TRUE	/	TRUE	1813824768	datr	Am0NarCbtrO3kM8EoV0yUebk
.instagram.com	TRUE	/	TRUE	1810800768	ig_did	C7DDFEB1-2885-420B-94DD-F7D8EC1F97A2
.instagram.com	TRUE	/	TRUE	1813824770	mid	ag1tAgALAAGhDcctA1vXQ2Upf8VV
.instagram.com	TRUE	/	TRUE	1810801003	ig_nrcb	1
.instagram.com	TRUE	/	TRUE	1815931110	csrftoken	OrqNykD3Gb1pJiBjSyg2CM5vJjN0sYox
.instagram.com	TRUE	/	TRUE	1789147110	ds_user_id	3451809639
.instagram.com	TRUE	/	TRUE	1815064568	ps_l	1
.instagram.com	TRUE	/	TRUE	1815064568	ps_n	1
.instagram.com	TRUE	/	TRUE	1781975901	dpr	1.25
.instagram.com	TRUE	/	TRUE	1781975901	wd	1536x730
.instagram.com	TRUE	/	TRUE	1812907108	sessionid	3451809639%3AWAxqvhVTTYtfF6%3A5%3AAYhfota3X01V0L6QarLueIUd1VBXaecEunbdmKuMTw
.instagram.com	TRUE	/	TRUE	0	rur	""EAG\0543451809639\0541812907109:01ffe9d7c9f1abf78a1f0f787c2dd1e1557976f3f8864237c68f6275f5eb88baa8cf1ffc""

.youtube.com	TRUE	/	TRUE	1791797622	__Secure-BUCKET	CAw
.youtube.com	TRUE	/	TRUE	1815924966	PREF	f4=4000000&tz=Asia.Calcutta&f7=100&f6=40000000
.youtube.com	TRUE	/	TRUE	1812728082	__Secure-1PSIDTS	sidts-CjQByojQU0HCLzlgpUoCJC9T5IxjHbFxCf4wBfqjhjmhjwSHP8ex-mz7tXETg-aA0zOXo1rOEAA
.youtube.com	samples	TRUE	/	TRUE	1812728082	__Secure-3PSIDTS	sidts-CjQByojQU0HCLzlgpUoCJC9T5IxjHbFxCf4wBfqjhjmhjwSHP8ex-mz7tXETg-aA0zOXo1rOEAA
.youtube.com	TRUE	/	TRUE	1796916927	VISITOR_INFO1_LIVE	McWuFHoNNUQ
.youtube.com	TRUE	/	TRUE	1796916927	VISITOR_PRIVACY_METADATA	CgJJThIEGgAgPw%3D%3D
.youtube.com	TRUE	/	TRUE	1796916924	__Secure-YNID	19.YT=lOGJY-imPO2kWZgzPhGn0bQgY3SHKS3ak5fZ_TECWuuY79khVfOo_2WMrVZbtUZ6syO9Kt4VuEwbueeBdYhgwInhlL9QwdRZ08SzaxpFjPA_fH0UB1RLdtMyqhPz9dGQy7fHFeMkSH6lVv5PNNNO2dFIFMNcL8BCEHiG4wenog1JZQb9WTEB3wRXeMyZ5bIlA_pz6SofsIxzTYf-C-GF09wcwdeuBcYeMBjO7yV2yTM9JKIqfbgmdct0U3OJnQVgYemZXL3AloEM6pA2s4_0ysLS0is6v0EWU5M1z2wr4DN74jVwNIHwXiRT_iPYQKpb-dDGVdvp-rbpe3iIRhnR8Q
.youtube.com	TRUE	/	TRUE	0	YSC	5mH2Uy3MXPE
.youtube.com	TRUE	/	TRUE	1796916924	__Secure-ROLLOUT_TOKEN	CLTCt4SPpaes2gEQjYbE0sbvkwMY0tP0u8WElQM%3D";

            // 2. ரன்டைமில் தற்காலிகமாக ஒரு குக்கீஸ் ஃபைலை சர்வருக்குள் உருவாக்குகிறோம்
            string tempCookiesPath = Path.Combine(Path.GetTempPath(), "runtime-cookies.txt");
            File.WriteAllText(tempCookiesPath, myCookiesContent);

            // 3. இறுதி கமாண்டுகளைச் சேர்க்கிறோம்
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

            return (process.ExitCode, string.IsNullOrEmpty(output) ? error : output);
        }
    }
}