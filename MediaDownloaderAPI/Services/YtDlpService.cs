using System.Diagnostics;
using MediaDownloaderAPI.Models;

namespace MediaDownloaderAPI.Services;

public class YtDlpService
{
    private readonly string _ffmpegPath = @"C:\ffmpeg\bin\ffmpeg.exe";
    private readonly string _downloadFolder;

    public YtDlpService()
    {
        _downloadFolder = Path.Combine(Path.GetTempPath(), "MediaDownloader");
        Directory.CreateDirectory(_downloadFolder);
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

            // Get available formats
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

    public async Task<string?> DownloadVideoAsync(string url, string quality)
    {
        var height = quality.Replace("p", "").Trim();
        var format = height == "best"
            ? "bestvideo+bestaudio/best"
            : $"bestvideo[height<={height}]+bestaudio/best";

        var outputTemplate = Path.Combine(_downloadFolder, "%(title)s.%(ext)s");
        var args = $"--no-playlist --merge-output-format mp4 --ffmpeg-location \"{_ffmpegPath}\" -f \"{format}\" -o \"{outputTemplate}\" \"{url}\"";

        Console.WriteLine($"[Download] Starting: {quality} | {url}");
        Console.WriteLine($"[Download] Args: {args}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        // Live output
        process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("[yt-dlp] " + e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("[yt-dlp] " + e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        Console.WriteLine($"[Download] Exit code: {process.ExitCode}");

        var file = new DirectoryInfo(_downloadFolder)
            .GetFiles("*.mp4")
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        Console.WriteLine($"[Download] File: {file?.FullName ?? "NOT FOUND"}");

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


public async Task<string?> DownloadMp3Async(string url, string title)
    {
        var outputTemplate = Path.Combine(_downloadFolder, "%(title)s.%(ext)s");

        var args = $"--no-playlist -x --audio-format mp3 --audio-quality 0 --ffmpeg-location \"{_ffmpegPath}\" -o \"{outputTemplate}\" \"{url}\"";

        Console.WriteLine($"[MP3] Starting: {url}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("[yt-dlp] " + e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("[yt-dlp] " + e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        Console.WriteLine($"[MP3] Exit code: {process.ExitCode}");

        var file = new DirectoryInfo(_downloadFolder)
            .GetFiles("*.mp3")
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        Console.WriteLine($"[MP3] File: {file?.FullName ?? "NOT FOUND"}");
        return file?.FullName;
    }

}