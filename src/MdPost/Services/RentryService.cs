namespace MdPost.Services;

public sealed class RentryService : IPasteService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://rentry.co/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public string Name => "rentry";

    public async Task<PasteResult> UploadAsync(string content, string? slug = null)
    {
        // Rentry requires a CSRF token from the main page
        var csrfToken = await GetCsrfTokenAsync();

        var form = new Dictionary<string, string>
        {
            ["csrfmiddlewaretoken"] = csrfToken,
            ["text"] = content,
            ["edit_code"] = "",  // Let rentry generate one
        };

        if (slug is not null)
            form["url"] = slug;

        var request = new HttpRequestMessage(HttpMethod.Post, "api/new")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Add("Cookie", $"csrftoken={csrfToken}");
        request.Headers.Add("Referer", "https://rentry.co/");

        var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        var status = root.GetProperty("status").GetString();
        if (status != "200")
        {
            var errors = root.TryGetProperty("errors", out var errProp)
                ? errProp.GetString() ?? "Unknown error"
                : "Upload failed";
            throw new InvalidOperationException($"Rentry error: {errors}");
        }

        var url = root.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("No URL in rentry response");
        var editCode = root.GetProperty("edit_code").GetString();

        return new PasteResult
        {
            Url = url,
            EditCode = editCode,
            Backend = Name
        };
    }

    public async Task<bool> DeleteAsync(string editCode, string url)
    {
        // Rentry doesn't have a delete API — edits only
        // Could blank the content as a workaround
        try
        {
            var csrfToken = await GetCsrfTokenAsync();
            var slug = new Uri(url).AbsolutePath.TrimStart('/');

            var form = new Dictionary<string, string>
            {
                ["csrfmiddlewaretoken"] = csrfToken,
                ["edit_code"] = editCode,
                ["text"] = "(deleted)",
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"api/edit/{slug}")
            {
                Content = new FormUrlEncodedContent(form)
            };
            request.Headers.Add("Cookie", $"csrftoken={csrfToken}");
            request.Headers.Add("Referer", "https://rentry.co/");

            var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> GetCsrfTokenAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "");
        var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // Extract csrftoken from Set-Cookie header
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                if (cookie.StartsWith("csrftoken=", StringComparison.Ordinal))
                {
                    var token = cookie.Split(';')[0]["csrftoken=".Length..];
                    return token;
                }
            }
        }

        throw new InvalidOperationException("Could not obtain CSRF token from rentry.co");
    }
}
