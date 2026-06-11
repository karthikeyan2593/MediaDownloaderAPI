namespace MediaDownloaderAPI.Models;

public class DownloadRequest
{
    public string Url { get; set; } = string.Empty;
    public string Quality { get; set; } = "best";

}

public class DownloadResponse
{
    public bool Success { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Title { get; set; }
    public string? Thumbnail { get; set; }
    public string? Error { get; set; }


    public List<string> AvailableQualities { get; set; } = new();
}