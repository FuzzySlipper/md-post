using System.Net.Http.Headers;

namespace MdPost.Services;

public sealed class PasteRsService : IPasteService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://paste.rs/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public string Name => "paste.rs";

    public async Task<PasteResult> UploadAsync(string content, string? slug = null)
    {
        var httpContent = new StringContent(content);
        httpContent.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");

        var response = await Http.PostAsync("", httpContent);
        response.EnsureSuccessStatusCode();

        var pasteUrl = (await response.Content.ReadAsStringAsync()).Trim();

        // Append .md for rendered markdown view
        if (!pasteUrl.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            pasteUrl += ".md";

        return new PasteResult
        {
            Url = pasteUrl,
            EditCode = null, // paste.rs doesn't provide edit codes
            Backend = Name
        };
    }

    public Task<bool> DeleteAsync(string editCode, string url)
    {
        // paste.rs doesn't support deletion
        return Task.FromResult(false);
    }
}
