using MediaDownloaderAPI.Models;
using MediaDownloaderAPI.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text.Json;

namespace MediaDownloaderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DownloadController : ControllerBase
    {
        private readonly YtDlpService _ytDlp;

        public DownloadController(YtDlpService ytDlp)
        {
            _ytDlp = ytDlp;
        }

        // 1. RapidAPI மூலம் வீடியோ தகவலைப் பெறுதல்
        [HttpPost("info")]
        public async Task<IActionResult> GetInfo([FromBody] DownloadRequest req)
        {
            if (string.IsNullOrEmpty(req.Url)) return BadRequest("URL missing");

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://social-download-all-in-one.p.rapidapi.com/v1/social/autolink");

            request.Headers.Add("x-rapidapi-key", "634d66a1fbmsh348e46cbbe59b16p1531e3jsnea49dffe631");
            request.Headers.Add("x-rapidapi-host", "social-download-all-in-one.p.rapidapi.com");

            var content = new StringContent(JsonSerializer.Serialize(new { url = req.Url }), System.Text.Encoding.UTF8, "application/json");
            request.Content = content;

            var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            return Content(responseBody, "application/json");
        }

        // 2. டவுன்லோட் ப்ராக்ரஸ் (Server-Sent Events)
        [HttpGet("progress")]
        public async Task DownloadWithProgress([FromQuery] string url, [FromQuery] string quality, [FromQuery] string title)
        {
            Response.Headers["Content-Type"] = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            var height = quality.Replace("p", "").Trim();
            var format = height == "best" ? "bestvideo+bestaudio/best" : $"bestvideo[height<={height}]+bestaudio/best";

            var downloadFolder = Path.Combine(Path.GetTempPath(), "MediaDownloader");
            var outputTemplate = Path.Combine(downloadFolder, "%(title)s.%(ext)s");
            var args = $"--no-playlist --merge-output-format mp4 --ffmpeg-location \"{_ytDlp.FfmpegPath}\" -f \"{format}\" -o \"{outputTemplate}\" \"{url}\"";

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

            process.OutputDataReceived += async (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var match = System.Text.RegularExpressions.Regex.Match(e.Data, @"\[download\]\s+([\d.]+)%");
                if (match.Success)
                {
                    await Response.WriteAsync($"data: {{\"type\":\"progress\",\"percent\":{match.Groups[1].Value}}}\n\n");
                    await Response.Body.FlushAsync();
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var file = new DirectoryInfo(downloadFolder).GetFiles("*.mp4").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            if (file != null)
                await Response.WriteAsync($"data: {{\"type\":\"done\",\"filename\":\"{Uri.EscapeDataString(file.Name)}\"}}\n\n");
            else
                await Response.WriteAsync($"data: {{\"type\":\"error\",\"message\":\"Download failed\"}}\n\n");

            await Response.Body.FlushAsync();
        }

        // 3. பைல் டவுன்லோட்
        [HttpGet("file")]
        public IActionResult GetFile([FromQuery] string filename)
        {
            var path = Path.Combine(Path.GetTempPath(), "MediaDownloader", Uri.UnescapeDataString(filename));
            if (!System.IO.File.Exists(path)) return NotFound();

            var stream = System.IO.File.OpenRead(path);
            Response.OnCompleted(async () => { stream.Close(); await Task.Delay(1000); if (System.IO.File.Exists(path)) System.IO.File.Delete(path); });
            return File(stream, "video/mp4", Uri.UnescapeDataString(filename));
        }

        // 4. MP3 டவுன்லோட்
        [HttpGet("mp3")]
        public async Task<IActionResult> DownloadMp3([FromQuery] string url, [FromQuery] string title)
        {
            if (string.IsNullOrEmpty(url)) return BadRequest("URL required");
            var (exitCode, output) = await _ytDlp.DownloadMp3Async(url, "mp3");
            if (exitCode != 0) return BadRequest($"MP3 conversion failed: {output}");

            var filePath = output.Trim();
            if (!System.IO.File.Exists(filePath)) return BadRequest("MP3 file not found");

            var stream = System.IO.File.OpenRead(filePath);
            Response.OnCompleted(async () => { stream.Close(); await Task.Delay(1000); if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath); });
            return File(stream, "audio/mpeg", Path.GetFileName(filePath));
        }
    }
}