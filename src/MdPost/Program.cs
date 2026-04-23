using MdPost.Data;
using MdPost.Services;
using MdPost.Tui;
using Terminal.Gui;

var appConfig = MdPostConfig.Load();
var blogConfig = BlogService.LoadConfig();
var cfConfig = CloudflarePagesService.LoadConfig();
IPasteService[] pasteServices = [new RentryService(), new PasteRsService(), new BlogService(blogConfig), new CloudflarePagesService(cfConfig)];
var defaultBackend = GetDefaultBackend(appConfig, pasteServices);

// Route commands
var command = args.Length > 0 ? args[0] : null;

switch (command)
{
    case "upload":
        return await CliUpload(args, await CreateRepoAsync(), pasteServices, defaultBackend);
    case "list":
        return await CliList(args, await CreateRepoAsync());
    case "search":
        return await CliSearch(args, await CreateRepoAsync());
    case "url":
        return await CliUrl(args, await CreateRepoAsync());
    case "default-backend":
        return CliDefaultBackend(args, appConfig, pasteServices);
    case "blog-init":
        return CliBlogInit(args);
    case "blog-theme":
        return CliBlogTheme(args);
    case "cf-blog-init":
        return CliCfBlogInit(args);
    case "help" or "--help" or "-h":
        PrintHelp();
        return 0;
    default:
        return RunTui(await CreateRepoAsync(), pasteServices, defaultBackend);
}

static async Task<PostRepository> CreateRepoAsync()
{
    var dbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".md-post", "mdpost.db");

    var dbInit = new DatabaseInitializer(dbPath);
    await dbInit.InitializeAsync();

    var db = new DbConnectionFactory(dbInit.ConnectionString);
    return new PostRepository(db);
}

static int RunTui(PostRepository repo, IPasteService[] pasteServices, string defaultBackend)
{
    Application.Init();
    try
    {
        var dashboard = new DashboardView(repo, pasteServices, defaultBackend);
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

static async Task<int> CliUpload(string[] args, PostRepository repo, IPasteService[] pasteServices, string defaultBackend)
{
    string? filePath = null;
    string? title = null;
    string? tagsStr = null;
    string backend = defaultBackend;
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
        Console.Error.WriteLine("Usage: mdpost upload <file> [--title <title>] [--tags <t1,t2>] [--backend rentry|paste.rs|blog|cf-blog] [--local] [--stdin]");
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
        var service = FindPasteService(pasteServices, backend);
        if (service is null)
        {
            Console.Error.WriteLine($"Unknown backend '{backend}'. Available backends: {FormatBackendList(pasteServices)}");
            return 1;
        }

        try
        {
            var uploadContent = UploadContentBuilder.Prepare(service.Name, title, tags, content);
            var result = await service.UploadAsync(uploadContent, slug);
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

static int CliDefaultBackend(string[] args, MdPostConfig config, IPasteService[] pasteServices)
{
    if (args.Length == 1)
    {
        Console.WriteLine(GetDefaultBackend(config, pasteServices));
        return 0;
    }

    if (args[1] is "--list" or "-l")
    {
        Console.WriteLine(FormatBackendList(pasteServices));
        return 0;
    }

    var service = FindPasteService(pasteServices, args[1]);
    if (service is null)
    {
        Console.Error.WriteLine($"Unknown backend '{args[1]}'. Available backends: {FormatBackendList(pasteServices)}");
        return 1;
    }

    config.DefaultBackend = service.Name;
    MdPostConfig.Save(config);

    Console.WriteLine($"Default backend set to: {service.Name}");
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

static int CliBlogInit(string[] args)
{
    string? repoPath = null;
    string? baseUrl = null;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--repo" when i + 1 < args.Length:
                repoPath = args[++i];
                break;
            case "--url" when i + 1 < args.Length:
                baseUrl = args[++i];
                break;
        }
    }

    repoPath ??= Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".md-post", "blog");
    baseUrl ??= "https://fuzzyslipper.github.io/blog";

    Console.WriteLine($"Blog repo path: {repoPath}");
    Console.WriteLine($"Blog base URL:  {baseUrl}");

    var config = new BlogConfig
    {
        RepoPath = Path.GetFullPath(repoPath),
        BaseUrl = baseUrl
    };
    BlogService.SaveConfig(config);

    Console.WriteLine("Blog config saved. Use `mdpost default-backend blog` to make it the default upload target.");
    Console.WriteLine("Run `mdpost blog-theme` to install the bundled dark blog theme.");
    return 0;
}

static int CliBlogTheme(string[] args)
{
    string? repoPath = null;

    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] is "--repo" && i + 1 < args.Length)
            repoPath = args[++i];
    }

    repoPath ??= BlogService.LoadConfig().RepoPath;
    if (string.IsNullOrWhiteSpace(repoPath))
    {
        Console.Error.WriteLine("Usage: mdpost blog-theme [--repo <path-to-local-clone>]");
        Console.Error.WriteLine("Configure a blog repo first with `mdpost blog-init`, or pass --repo explicitly.");
        return 1;
    }

    try
    {
        var writtenFiles = BlogThemeInstaller.Install(repoPath);
        Console.WriteLine($"Installed blog theme in: {Path.GetFullPath(repoPath)}");
        foreach (var file in writtenFiles)
            Console.WriteLine($"  updated {file}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Theme install failed: {ex.Message}");
        return 1;
    }

    return 0;
}

static int CliCfBlogInit(string[] args)
{
    string? repoPath = null;
    string? baseUrl = null;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--repo" when i + 1 < args.Length:
                repoPath = args[++i];
                break;
            case "--url" when i + 1 < args.Length:
                baseUrl = args[++i];
                break;
        }
    }

    if (repoPath is null || baseUrl is null)
    {
        Console.Error.WriteLine("Usage: mdpost cf-blog-init --repo <path-to-local-clone> --url <site-base-url>");
        return 1;
    }

    Console.WriteLine($"CF blog repo path: {repoPath}");
    Console.WriteLine($"CF blog base URL:  {baseUrl}");

    var config = new CfPagesConfig
    {
        RepoPath = Path.GetFullPath(repoPath),
        BaseUrl = baseUrl
    };
    CloudflarePagesService.SaveConfig(config);

    Console.WriteLine("Cloudflare Pages blog config saved. Use `mdpost default-backend cf-blog` to make it the default upload target.");
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
          mdpost default-backend    Show or set the default upload backend
          mdpost blog-init          Configure the blog backend
          mdpost blog-theme         Install the bundled dark blog theme
          mdpost help               Show this help

        Upload options:
          --title, -t <title>       Post title (defaults to filename)
          --tags <t1,t2>            Comma-separated tags
          --backend, -b <name>      rentry, paste.rs, blog, cf-blog
          --local                   Save locally only, don't upload
          --stdin                   Read content from stdin

        Backend defaults:
          mdpost default-backend                Print the current default backend
          mdpost default-backend blog           Save blog as the default target
          mdpost default-backend --list         Show all available backends

        Blog setup:
          mdpost blog-init [--repo <path>] [--url <base-url>]
          mdpost blog-theme [--repo <path>]
          mdpost cf-blog-init --repo <path> --url <base-url>
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

static IPasteService? FindPasteService(IEnumerable<IPasteService> pasteServices, string backendName)
{
    return pasteServices.FirstOrDefault(service =>
        string.Equals(service.Name, backendName, StringComparison.OrdinalIgnoreCase));
}

static string GetDefaultBackend(MdPostConfig config, IEnumerable<IPasteService> pasteServices)
{
    return FindPasteService(pasteServices, config.DefaultBackend)?.Name
        ?? pasteServices.First().Name;
}

static string FormatBackendList(IEnumerable<IPasteService> pasteServices) =>
    string.Join(", ", pasteServices.Select(service => service.Name));
