using System.Diagnostics;
using System.Text.Json;

namespace MdPost.Services;

public sealed class BlogConfig
{
    public string RepoPath { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}

public sealed class BlogService : IPasteService
{
    private readonly BlogConfig _config;

    public BlogService(BlogConfig config)
    {
        _config = config;
    }

    public string Name => "blog";

    public async Task<PasteResult> UploadAsync(string content, string? slug = null)
    {
        if (string.IsNullOrEmpty(_config.RepoPath))
            throw new InvalidOperationException(
                "Blog not configured. Run: mdpost blog-init");

        var postsDir = Path.Combine(_config.RepoPath, "_posts");
        Directory.CreateDirectory(postsDir);

        // Pull latest before writing
        await GitAsync(_config.RepoPath, "pull --rebase --quiet");

        // Parse any existing frontmatter from the content, or generate it
        var (frontmatter, body) = SplitFrontmatter(content);
        var title = frontmatter.GetValueOrDefault("title") ?? slug ?? "Untitled";
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        slug ??= Slugify(title);

        // Build the Jekyll post
        var postFilename = $"{date}-{slug}.md";
        var postPath = Path.Combine(postsDir, postFilename);

        var jekyllContent = $"""
            ---
            title: "{EscapeYaml(title)}"
            date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} +0000
            {(frontmatter.ContainsKey("tags") ? $"tags: {FormatTags(frontmatter["tags"])}" : "")}
            ---

            {body}
            """;

        // Fix indentation from raw string literal
        jekyllContent = string.Join('\n', jekyllContent.Split('\n').Select(l => l.TrimStart()));

        await File.WriteAllTextAsync(postPath, jekyllContent);

        // Commit and push
        await GitAsync(_config.RepoPath, $"add \"{postPath}\"");
        await GitAsync(_config.RepoPath, $"commit -m \"publish: {EscapeShell(title)}\"");
        await GitAsync(_config.RepoPath, "push --quiet");

        var url = $"{_config.BaseUrl.TrimEnd('/')}/{slug}/";

        return new PasteResult
        {
            Url = url,
            EditCode = slug, // The slug serves as the edit key — we can overwrite the file
            Backend = Name
        };
    }

    public async Task<bool> DeleteAsync(string editCode, string url)
    {
        if (string.IsNullOrEmpty(_config.RepoPath)) return false;

        var postsDir = Path.Combine(_config.RepoPath, "_posts");
        var slug = editCode; // editCode is the slug for blog posts

        // Find the post file (date prefix varies)
        var matches = Directory.GetFiles(postsDir, $"*-{slug}.md");
        if (matches.Length == 0) return false;

        foreach (var match in matches)
            File.Delete(match);

        try
        {
            await GitAsync(_config.RepoPath, "add -A");
            await GitAsync(_config.RepoPath, $"commit -m \"delete: {EscapeShell(slug)}\"");
            await GitAsync(_config.RepoPath, "push --quiet");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (Dictionary<string, string> frontmatter, string body) SplitFrontmatter(string content)
    {
        var fm = new Dictionary<string, string>();
        var trimmed = content.TrimStart();

        if (!trimmed.StartsWith("---"))
            return (fm, content);

        var endIdx = trimmed.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIdx < 0)
            return (fm, content);

        var fmBlock = trimmed[3..endIdx].Trim();
        var body = trimmed[(endIdx + 3)..].TrimStart('\r', '\n');

        foreach (var line in fmBlock.Split('\n'))
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;
            var key = line[..colonIdx].Trim();
            var value = line[(colonIdx + 1)..].Trim().Trim('"');
            fm[key] = value;
        }

        return (fm, body);
    }

    private static async Task GitAsync(string workDir, string args)
    {
        var psi = new ProcessStartInfo("git")
        {
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git");

        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"git {args} failed: {stderr.Trim()}");
        }
    }

    private static string Slugify(string title)
    {
        var slug = title.ToLowerInvariant().Replace(' ', '-').Replace("_", "-");
        slug = new string(slug.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        return slug;
    }

    private static string EscapeYaml(string s) => s.Replace("\"", "\\\"");
    private static string EscapeShell(string s) => s.Replace("\"", "\\\"");

    private static string FormatTags(string tagsValue)
    {
        // Normalize: strip existing brackets, split, re-wrap
        var stripped = tagsValue.Trim().Trim('[', ']');
        var tags = stripped.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return $"[{string.Join(", ", tags)}]";
    }

    // --- Config persistence ---

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".md-post", "blog-config.json");

    public static BlogConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
            return new BlogConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<BlogConfig>(json) ?? new BlogConfig();
    }

    public static void SaveConfig(BlogConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
