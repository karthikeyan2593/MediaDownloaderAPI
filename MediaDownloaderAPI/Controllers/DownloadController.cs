using Microsoft.AspNetCore.Mvc;
using MediaDownloaderAPI.Models;
using MediaDownloaderAPI.Services;

namespace MediaDownloaderAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DownloadController : ControllerBase
{
    private readonly YtDlpService _ytDlp;

    public DownloadController(YtDlpService ytDlp)
    {
        _ytDlp = ytDlp;
    }

    [HttpPost("info")]
    public async Task<IActionResult> GetInfo([FromBody] DownloadRequest request)
    {
        if (string.IsNullOrEmpty(request.Url))
            return BadRequest("URL required");

        if (!IsSupported(request.Url))
            return BadRequest("Only Instagram, YouTube, Facebook supported");

        var result = await _ytDlp.GetVideoInfoAsync(request.Url);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpGet("start")]
    public async Task<IActionResult> StartDownload([FromQuery] string url, [FromQuery] string quality, [FromQuery] string title)
    {
        var filePath = await _ytDlp.DownloadVideoAsync(url, quality);

        if (filePath == null || !System.IO.File.Exists(filePath))
            return BadRequest("Download failed");

        var fileName = Path.GetFileName(filePath);
        var stream = System.IO.File.OpenRead(filePath);

        Response.OnCompleted(async () =>
        {
            stream.Close();
            await Task.Delay(500);
            System.IO.File.Delete(filePath);
        });

        return File(stream, "video/mp4", fileName);
    }

    [HttpGet("mp3")]
    public async Task<IActionResult> DownloadMp3([FromQuery] string url, [FromQuery] string title)
    {
        if (string.IsNullOrEmpty(url))
            return BadRequest("URL required");

        var filePath = await _ytDlp.DownloadMp3Async(url, title);

        if (filePath == null || !System.IO.File.Exists(filePath))
            return BadRequest("MP3 conversion failed");

        var fileName = Path.GetFileName(filePath);
        var stream = System.IO.File.OpenRead(filePath);

        Response.OnCompleted(async () =>
        {
            stream.Close();
            await Task.Delay(500);
            System.IO.File.Delete(filePath);
        });

        return File(stream, "audio/mpeg", fileName);
    }

    private bool IsSupported(string url)
    {
        return url.Contains("instagram.com") ||
               url.Contains("youtube.com") ||
               url.Contains("youtu.be") ||
               url.Contains("facebook.com") ||
               url.Contains("fb.watch") ||
               url.Contains("tiktok.com") ||  // ← இது add பண்ணு
               url.Contains("vm.tiktok.com");  // ← இதுவும்
    }

}