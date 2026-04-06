namespace MdPost.Services;

public sealed class PasteResult
{
    public required string Url { get; init; }
    public string? EditCode { get; init; }
    public required string Backend { get; init; }
}

public interface IPasteService
{
    string Name { get; }
    Task<PasteResult> UploadAsync(string content, string? slug = null);
    Task<bool> DeleteAsync(string editCode, string url);
}
