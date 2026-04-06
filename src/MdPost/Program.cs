using MdPost.Data;
using MdPost.Services;
using MdPost.Tui;
using Terminal.Gui;

var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".md-post", "mdpost.db");

var dbInit = new DatabaseInitializer(dbPath);
await dbInit.InitializeAsync();

var db = new DbConnectionFactory(dbInit.ConnectionString);
var repo = new PostRepository(db);

IPasteService[] pasteServices = [new RentryService(), new PasteRsService()];

// Route commands
var command = args.Length > 0 ? args[0] : null;

switch (command)
{
    case "upload":
        return await CliUpload(args, repo, pasteServices);
    case "list":
        return await CliList(args, repo);
    case "search":
        return await CliSearch(args, repo);
    case "url":
        return await CliUrl(args, repo);
    case "help" or "--help" or "-h":
        PrintHelp();
        return 0;
    default:
        return RunTui(repo, pasteServices);
}

static int RunTui(PostRepository repo, IPasteService[] pasteServices)
{
    Application.Init();
    try
    {
        var dashboard = new DashboardView(repo, pasteServices);
        Application.Top.Add(dashboard);
        _ = dashboard.StartAsync();
        Application.Run();
    }
    finally
    {
        Application.Shutdown();
    }
    return 0;
}

static async Task<int> CliUpload(string[] args, PostRepository repo, IPasteService[] pasteServices)
{
    string? filePath = null;
    string? title = null;
    string? tagsStr = null;
    string backend = "rentry";
    bool localOnly = false;
    bool fromStdin = false;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--title" or "-t" when i + 1 < args.Length:
                title = args[++i];
                break;
            case "--tags" when i + 1 < args.Length:
                tagsStr = args[++i];
                break;
            case "--backend" or "-b" when i + 1 < args.Length:
                backend = args[++i];
                break;
            case "--local":
                localOnly = true;
                break;
            case "--stdin":
                fromStdin = true;
                break;
            default:
                if (!args[i].StartsWith('-'))
                    filePath = args[i];
                break;
        }
    }

    string content;
    string? sourcePath = null;

    if (fromStdin)
    {
        content = await Console.In.ReadToEndAsync();
    }
    else if (filePath is not null)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine($"File not found: {fullPath}");
            return 1;
        }
        content = await File.ReadAllTextAsync(fullPath);
        sourcePath = fullPath;
        title ??= Path.GetFileNameWithoutExtension(fullPath);
    }
    else
    {
        Console.Error.WriteLine("Usage: mdpost upload <file> [--title <title>] [--tags <t1,t2>] [--backend rentry|paste.rs] [--local] [--stdin]");
        return 1;
    }

    if (string.IsNullOrEmpty(content))
    {
        Console.Error.WriteLine("No content to upload.");
        return 1;
    }

    title ??= "Untitled";

    var tags = string.IsNullOrEmpty(tagsStr)
        ? null
        : tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToList();

    var slug = GenerateSlug(title);

    var post = new MdPost.Models.Post
    {
        Slug = slug,
        Title = title,
        Content = content,
        Tags = tags,
        SourcePath = sourcePath
    };

    if (!localOnly)
    {
        var service = pasteServices.FirstOrDefault(s => s.Name == backend) ?? pasteServices[0];
        try
        {
            var result = await service.UploadAsync(content);
            post.RemoteUrl = result.Url;
            post.EditCode = result.EditCode;
            post.Backend = result.Backend;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Upload failed: {ex.Message}");
            Console.Error.WriteLine("Saving locally only.");
        }
    }

    await repo.UpsertAsync(post);

    if (post.RemoteUrl is not null)
    {
        Console.WriteLine(post.RemoteUrl);
    }
    else
    {
        Console.WriteLine($"Saved locally as: {post.Slug}");
    }

    return 0;
}

static async Task<int> CliList(string[] args, PostRepository repo)
{
    string? tagsStr = null;
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] is "--tag" or "--tags" && i + 1 < args.Length)
            tagsStr = args[++i];
    }

    var tags = string.IsNullOrEmpty(tagsStr)
        ? null
        : tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var posts = await repo.ListAsync(tags);

    if (posts.Count == 0)
    {
        Console.WriteLine("No posts found.");
        return 0;
    }

    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"{"ID",-5} {"BACKEND",-10} {"TITLE",-35} {"TAGS",-20} {"URL"}");
    Console.WriteLine(new string('─', 100));
    Console.ResetColor();

    foreach (var p in posts)
    {
        var tags2 = p.Tags is { Count: > 0 } ? string.Join(",", p.Tags) : "";
        var url = p.RemoteUrl ?? "(local)";

        Console.Write($"{p.Id,-5} ");
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write($"{p.Backend ?? "local",-10} ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{Truncate(p.Title, 35),-35} ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"{Truncate(tags2, 20),-20} ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(url);
        Console.ResetColor();
    }

    return 0;
}

static async Task<int> CliSearch(string[] args, PostRepository repo)
{
    var query = args.Length > 1 ? string.Join(' ', args.Skip(1)) : null;
    if (string.IsNullOrEmpty(query))
    {
        Console.Error.WriteLine("Usage: mdpost search <query>");
        return 1;
    }

    var results = await repo.SearchAsync(query);
    if (results.Count == 0)
    {
        Console.WriteLine("No results found.");
        return 0;
    }

    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"Search results for \"{query}\":");
    Console.WriteLine(new string('─', 60));
    Console.ResetColor();

    foreach (var r in results)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"  {r.Title}");
        Console.ResetColor();
        if (r.RemoteUrl is not null)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"  {r.RemoteUrl}");
            Console.ResetColor();
        }
        Console.WriteLine();
        var snippet = r.Snippet.Replace("<b>", "").Replace("</b>", "");
        Console.WriteLine($"    {Truncate(snippet.ReplaceLineEndings(" "), 70)}");
        Console.WriteLine();
    }

    return 0;
}

static async Task<int> CliUrl(string[] args, PostRepository repo)
{
    var slug = args.Length > 1 ? args[1] : null;
    if (slug is null)
    {
        Console.Error.WriteLine("Usage: mdpost url <slug>");
        return 1;
    }

    var post = await repo.GetAsync(slug);
    if (post is null)
    {
        Console.Error.WriteLine($"Post '{slug}' not found.");
        return 1;
    }

    if (post.RemoteUrl is null)
    {
        Console.Error.WriteLine("This post hasn't been uploaded.");
        return 1;
    }

    Console.WriteLine(post.RemoteUrl);
    return 0;
}

static void PrintHelp()
{
    Console.WriteLine("""
        mdpost — Markdown paste & library manager

        Usage:
          mdpost                    Launch TUI dashboard
          mdpost upload <file>      Upload a markdown file
          mdpost upload --stdin     Upload from stdin
          mdpost list [--tag <t>]   List all posts, optionally filter by tag
          mdpost search <query>     Full-text search across posts
          mdpost url <slug>         Print the remote URL for a post
          mdpost help               Show this help

        Upload options:
          --title, -t <title>       Post title (defaults to filename)
          --tags <t1,t2>            Comma-separated tags
          --backend, -b <name>      Paste service: rentry (default), paste.rs
          --local                   Save locally only, don't upload
          --stdin                   Read content from stdin
        """);
}

static string GenerateSlug(string title)
{
    var slug = title.ToLowerInvariant()
        .Replace(' ', '-')
        .Replace("_", "-");
    slug = new string(slug.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
    while (slug.Contains("--"))
        slug = slug.Replace("--", "-");
    slug = slug.Trim('-');
    if (slug.Length > 60)
        slug = slug[..60].TrimEnd('-');
    slug += $"-{DateTime.UtcNow:yyMMdd}";
    return slug;
}

static string Truncate(string text, int maxLen) =>
    text.Length <= maxLen ? text : text[..(maxLen - 3)] + "...";
