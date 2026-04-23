namespace MdPost.Services;

public static class BlogThemeInstaller
{
    public static IReadOnlyList<string> Install(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            throw new InvalidOperationException("A blog repository path is required.");

        repoPath = Path.GetFullPath(repoPath);

        if (!Directory.Exists(repoPath))
            throw new DirectoryNotFoundException($"Blog repo not found: {repoPath}");

        var writtenFiles = new List<string>();

        Directory.CreateDirectory(Path.Combine(repoPath, "_layouts"));
        Directory.CreateDirectory(Path.Combine(repoPath, "assets", "css"));

        WriteFile(repoPath, "_layouts/default.html", DefaultLayout, writtenFiles);
        WriteFile(repoPath, "_layouts/home.html", HomeLayout, writtenFiles);
        WriteFile(repoPath, "_layouts/post.html", PostLayout, writtenFiles);
        WriteFile(repoPath, "assets/css/style.scss", StyleSheet, writtenFiles);

        var indexPath = Path.Combine(repoPath, "index.md");
        if (!File.Exists(indexPath))
            WriteFile(repoPath, "index.md", DefaultIndexPage, writtenFiles);

        EnsureConfig(repoPath, writtenFiles);

        return writtenFiles;
    }

    private static void EnsureConfig(string repoPath, List<string> writtenFiles)
    {
        var configPath = Path.Combine(repoPath, "_config.yml");
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, DefaultConfig);
            writtenFiles.Add("_config.yml");
            return;
        }

        var original = File.ReadAllText(configPath);
        var updated = original.TrimEnd();

        updated = EnsureScalar(updated, "markdown", "kramdown");
        updated = EnsureScalar(updated, "highlighter", "rouge");

        if (!ContainsTopLevelKey(updated, "kramdown"))
        {
            updated += """

                kramdown:
                  input: GFM
                  syntax_highlighter: rouge
                """;
        }

        if (!updated.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            updated += Environment.NewLine;

        if (!string.Equals(original, updated, StringComparison.Ordinal))
        {
            File.WriteAllText(configPath, updated);
            writtenFiles.Add("_config.yml");
        }
    }

    private static string EnsureScalar(string yaml, string key, string value)
    {
        if (ContainsTopLevelKey(yaml, key))
            return yaml;

        return $"{yaml}{Environment.NewLine}{key}: {value}";
    }

    private static bool ContainsTopLevelKey(string yaml, string key)
    {
        return yaml.Split('\n').Any(line =>
            !string.IsNullOrWhiteSpace(line) &&
            !char.IsWhiteSpace(line[0]) &&
            line.StartsWith($"{key}:", StringComparison.Ordinal));
    }

    private static void WriteFile(string repoPath, string relativePath, string content, List<string> writtenFiles)
    {
        var fullPath = Path.Combine(repoPath, relativePath);
        File.WriteAllText(fullPath, content);
        writtenFiles.Add(relativePath);
    }

    private const string DefaultIndexPage = """
        ---
        layout: home
        title: Posts
        ---
        """;

    private const string DefaultConfig = """
        title: Blog
        description: Markdown posts published via mdpost
        theme: minima
        permalink: /:slug/
        markdown: kramdown
        highlighter: rouge

        kramdown:
          input: GFM
          syntax_highlighter: rouge

        defaults:
          - scope:
              path: "_posts"
              type: posts
            values:
              layout: post
        """;

    private const string DefaultLayout = """
        <!DOCTYPE html>
        <html lang="{{ page.lang | default: site.lang | default: 'en' }}">
          <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{% if page.title and page.title != site.title %}{{ page.title | escape }} | {{ site.title | escape }}{% else %}{{ site.title | default: page.title | escape }}{% endif %}</title>
            {% assign meta_description = page.description | default: page.excerpt | default: site.description %}
            <meta name="description" content="{{ meta_description | strip_html | strip_newlines | escape }}">
            <meta name="color-scheme" content="dark">
            <link rel="canonical" href="{{ page.url | absolute_url }}">
            <link rel="stylesheet" href="{{ '/assets/css/style.css' | relative_url }}">
          </head>
          <body>
            <div class="page-glow page-glow--top"></div>
            <div class="page-glow page-glow--bottom"></div>

            <header class="site-shell">
              <div class="site-header">
                <a class="brand" href="{{ '/' | relative_url }}">
                  <span class="brand__label">{{ site.title | default: 'Blog' }}</span>
                  {% if site.description %}
                    <span class="brand__tagline">{{ site.description }}</span>
                  {% endif %}
                </a>
              </div>
            </header>

            <main class="site-shell">
              {{ content }}
            </main>

            <footer class="site-shell site-footer">
              <p>Published with mdpost and styled for late-night reading.</p>
            </footer>
          </body>
        </html>
        """;

    private const string HomeLayout = """
        <section class="hero">
          <p class="eyebrow">Markdown posts, research notes, and quick dispatches</p>
          <h1>{{ page.title | default: site.title }}</h1>
          {% if site.description %}
            <p class="hero__lede">{{ site.description }}</p>
          {% endif %}
        </section>

        {% if site.posts.size > 0 %}
          <section class="post-grid">
            {% for post in site.posts %}
              {% assign excerpt = post.excerpt | strip_html | strip_newlines | truncate: 220 %}
              <article class="post-card">
                <p class="post-card__meta">{{ post.date | date: "%B %-d, %Y" }}</p>
                <h2><a href="{{ post.url | relative_url }}">{{ post.title }}</a></h2>

                {% if post.tags and post.tags.size > 0 %}
                  <p class="tag-row">
                    {% for tag in post.tags %}
                      <span class="tag-pill">{{ tag }}</span>
                    {% endfor %}
                  </p>
                {% endif %}

                {% if excerpt != "" %}
                  <p class="post-card__excerpt">{{ excerpt }}</p>
                {% endif %}

                <p class="post-card__cta"><a href="{{ post.url | relative_url }}">Read post</a></p>
              </article>
            {% endfor %}
          </section>
        {% else %}
          <section class="empty-state">
            <p>No posts yet. Publish your first one with <code>mdpost upload --backend blog</code>.</p>
          </section>
        {% endif %}
        """;

    private const string PostLayout = """
        <article class="post-shell">
          <p class="back-link"><a href="{{ '/' | relative_url }}">All posts</a></p>

          <header class="post-hero">
            <p class="eyebrow">{{ page.date | date: "%B %-d, %Y" }}</p>
            <h1>{{ page.title | escape }}</h1>

            {% if page.tags and page.tags.size > 0 %}
              <p class="tag-row">
                {% for tag in page.tags %}
                  <span class="tag-pill">{{ tag }}</span>
                {% endfor %}
              </p>
            {% endif %}
          </header>

          <div class="post-content">
            {{ content }}
          </div>
        </article>
        """;

    private const string StyleSheet = """
        ---
        ---
        :root {
          color-scheme: dark;
          --bg: #090d16;
          --bg-deep: #05070d;
          --panel: rgba(10, 16, 29, 0.82);
          --panel-strong: rgba(7, 12, 24, 0.9);
          --border: rgba(148, 163, 184, 0.16);
          --border-strong: rgba(96, 165, 250, 0.24);
          --text: #d9e2f0;
          --muted: #94a3b8;
          --heading: #f8fbff;
          --accent: #7dd3fc;
          --accent-strong: #38bdf8;
          --accent-pink: #f472b6;
          --accent-gold: #f59e0b;
          --shadow: 0 30px 80px rgba(0, 0, 0, 0.42);
          --radius: 24px;
          --radius-sm: 14px;
          --content-width: min(74rem, calc(100vw - 2.5rem));
          --prose-width: min(72ch, 100%);
          --body-font: "Iowan Old Style", "Palatino Linotype", "Book Antiqua", Georgia, serif;
          --ui-font: "Avenir Next", "Segoe UI", "Trebuchet MS", sans-serif;
          --mono-font: "JetBrains Mono", "Cascadia Code", "SFMono-Regular", Consolas, monospace;
        }

        * {
          box-sizing: border-box;
        }

        html {
          min-height: 100%;
          scroll-behavior: smooth;
        }

        body {
          margin: 0;
          min-height: 100vh;
          background:
            radial-gradient(circle at top right, rgba(56, 189, 248, 0.12), transparent 34rem),
            radial-gradient(circle at bottom left, rgba(244, 114, 182, 0.1), transparent 30rem),
            linear-gradient(180deg, #0b1220 0%, var(--bg) 42%, var(--bg-deep) 100%);
          color: var(--text);
          font-family: var(--body-font);
          font-size: 18px;
          line-height: 1.78;
          text-rendering: optimizeLegibility;
        }

        ::selection {
          background: rgba(56, 189, 248, 0.24);
          color: #ffffff;
        }

        a {
          color: var(--accent);
          text-decoration-thickness: 0.08em;
          text-underline-offset: 0.14em;
        }

        a:hover {
          color: #d6f5ff;
        }

        .site-shell {
          position: relative;
          width: var(--content-width);
          margin: 0 auto;
        }

        .site-header {
          display: flex;
          justify-content: flex-start;
          padding: 1.75rem 0 0.75rem;
        }

        .brand {
          display: inline-flex;
          flex-direction: column;
          gap: 0.18rem;
          max-width: min(100%, 48rem);
          padding: 0.95rem 1.2rem;
          border: 1px solid var(--border);
          border-radius: 999px;
          background: rgba(15, 23, 42, 0.68);
          box-shadow: var(--shadow);
          backdrop-filter: blur(18px);
          text-decoration: none;
        }

        .brand__label {
          color: var(--heading);
          font-family: var(--ui-font);
          font-size: 1rem;
          font-weight: 700;
          letter-spacing: 0.1em;
          text-transform: uppercase;
        }

        .brand__tagline {
          color: var(--muted);
          font-family: var(--ui-font);
          font-size: 0.92rem;
        }

        .hero,
        .post-shell {
          padding-top: 1.8rem;
        }

        .hero {
          padding-bottom: 1.5rem;
        }

        .eyebrow {
          margin: 0 0 0.65rem;
          color: var(--accent-pink);
          font-family: var(--ui-font);
          font-size: 0.76rem;
          font-weight: 700;
          letter-spacing: 0.18em;
          text-transform: uppercase;
        }

        .hero h1,
        .post-hero h1 {
          margin: 0;
          max-width: 14ch;
          color: var(--heading);
          font-size: clamp(2.4rem, 6vw, 4.8rem);
          line-height: 1.04;
          letter-spacing: -0.03em;
        }

        .hero__lede {
          max-width: 60ch;
          margin: 1rem 0 0;
          color: var(--muted);
          font-size: 1.08rem;
        }

        .post-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(18.5rem, 1fr));
          gap: 1.25rem;
          padding: 1rem 0 3rem;
        }

        .post-card,
        .post-content {
          border: 1px solid var(--border);
          box-shadow: var(--shadow);
          backdrop-filter: blur(16px);
        }

        .post-card {
          display: flex;
          flex-direction: column;
          gap: 0.85rem;
          padding: 1.35rem 1.4rem 1.45rem;
          border-radius: var(--radius);
          background: linear-gradient(180deg, rgba(15, 23, 42, 0.9), rgba(15, 23, 42, 0.72));
        }

        .post-card h2 {
          margin: 0;
          color: var(--heading);
          font-size: 1.4rem;
          line-height: 1.2;
        }

        .post-card h2 a {
          color: inherit;
          text-decoration: none;
        }

        .post-card h2 a:hover {
          color: var(--accent);
        }

        .post-card__meta,
        .post-card__excerpt,
        .site-footer {
          color: var(--muted);
        }

        .post-card__meta,
        .post-card__cta,
        .back-link {
          font-family: var(--ui-font);
        }

        .post-card__meta,
        .back-link {
          margin: 0;
          font-size: 0.9rem;
          letter-spacing: 0.02em;
        }

        .post-card__excerpt,
        .post-card__cta {
          margin: 0;
        }

        .post-card__cta {
          margin-top: auto;
          font-size: 0.95rem;
          font-weight: 700;
        }

        .tag-row {
          display: flex;
          flex-wrap: wrap;
          gap: 0.45rem;
          margin: 0;
        }

        .tag-pill {
          display: inline-flex;
          align-items: center;
          padding: 0.22rem 0.68rem;
          border: 1px solid rgba(125, 211, 252, 0.22);
          border-radius: 999px;
          background: rgba(56, 189, 248, 0.12);
          color: #d7f4ff;
          font-family: var(--ui-font);
          font-size: 0.78rem;
          letter-spacing: 0.03em;
        }

        .post-shell {
          padding-bottom: 4rem;
        }

        .post-hero {
          padding-bottom: 1rem;
        }

        .back-link {
          margin-bottom: 1.25rem;
        }

        .post-content {
          width: var(--prose-width);
          max-width: 100%;
          padding: clamp(1.25rem, 3vw, 2.3rem);
          border-radius: calc(var(--radius) + 4px);
          background: rgba(7, 12, 24, 0.74);
        }

        .post-content > :first-child {
          margin-top: 0;
        }

        .post-content > :last-child {
          margin-bottom: 0;
        }

        .post-content h2,
        .post-content h3,
        .post-content h4 {
          margin-top: 2.35rem;
          margin-bottom: 0.9rem;
          color: var(--heading);
          line-height: 1.2;
          scroll-margin-top: 1.5rem;
        }

        .post-content h2 {
          padding-bottom: 0.45rem;
          border-bottom: 1px solid var(--border);
          font-size: clamp(1.7rem, 2.7vw, 2.15rem);
        }

        .post-content h3 {
          font-size: clamp(1.35rem, 2vw, 1.65rem);
        }

        .post-content p,
        .post-content ul,
        .post-content ol,
        .post-content blockquote,
        .post-content table,
        .post-content pre,
        .post-content figure {
          margin: 1.15rem 0;
        }

        .post-content ul,
        .post-content ol {
          padding-left: 1.35rem;
        }

        .post-content li + li {
          margin-top: 0.45rem;
        }

        .post-content strong {
          color: var(--heading);
        }

        .post-content hr {
          border: 0;
          border-top: 1px solid var(--border);
          margin: 2rem 0;
        }

        .post-content blockquote {
          margin-left: 0;
          padding: 1rem 1.2rem;
          border-left: 4px solid var(--accent-strong);
          border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
          background: rgba(30, 41, 59, 0.46);
          color: #dbe5f7;
        }

        .post-content img {
          display: block;
          max-width: 100%;
          margin: 1.5rem auto;
          border: 1px solid rgba(148, 163, 184, 0.16);
          border-radius: 18px;
          box-shadow: 0 20px 50px rgba(0, 0, 0, 0.38);
        }

        .post-content table {
          width: 100%;
          border-collapse: collapse;
          overflow: hidden;
          border-radius: 16px;
          border: 1px solid var(--border);
        }

        .post-content thead th {
          color: var(--heading);
          background: rgba(30, 41, 59, 0.56);
          font-family: var(--ui-font);
          font-size: 0.92rem;
        }

        .post-content th,
        .post-content td {
          padding: 0.78rem 0.95rem;
          text-align: left;
          border-bottom: 1px solid var(--border);
        }

        .post-content tr:nth-child(even) td {
          background: rgba(15, 23, 42, 0.3);
        }

        .post-content :not(pre) > code {
          padding: 0.14rem 0.4rem;
          border: 1px solid rgba(148, 163, 184, 0.18);
          border-radius: 0.55rem;
          background: rgba(148, 163, 184, 0.14);
          color: #ffd7a0;
          font-family: var(--mono-font);
          font-size: 0.92em;
        }

        .highlighter-rouge,
        .highlight {
          margin: 1.35rem 0;
          overflow: hidden;
          border: 1px solid rgba(96, 165, 250, 0.16);
          border-radius: 18px;
          background: linear-gradient(180deg, rgba(12, 18, 32, 0.96), rgba(6, 10, 18, 0.96));
          box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04), 0 20px 60px rgba(0, 0, 0, 0.35);
        }

        .highlighter-rouge::before,
        .highlight::before {
          display: block;
          padding: 0.7rem 1rem;
          border-bottom: 1px solid rgba(96, 165, 250, 0.12);
          background: rgba(15, 23, 42, 0.94);
          color: #8ca1c4;
          content: "code";
          font-family: var(--ui-font);
          font-size: 0.76rem;
          font-weight: 700;
          letter-spacing: 0.14em;
          text-transform: uppercase;
        }

        .language-bash.highlighter-rouge::before,
        .language-shell.highlighter-rouge::before {
          content: "bash";
        }

        .language-c.highlighter-rouge::before,
        .language-cs.highlighter-rouge::before,
        .language-cpp.highlighter-rouge::before {
          content: "c-family";
        }

        .language-js.highlighter-rouge::before,
        .language-javascript.highlighter-rouge::before,
        .language-ts.highlighter-rouge::before {
          content: "javascript";
        }

        .language-json.highlighter-rouge::before,
        .language-yaml.highlighter-rouge::before,
        .language-yml.highlighter-rouge::before,
        .language-toml.highlighter-rouge::before {
          content: "data";
        }

        .language-html.highlighter-rouge::before,
        .language-xml.highlighter-rouge::before,
        .language-css.highlighter-rouge::before,
        .language-scss.highlighter-rouge::before {
          content: "markup";
        }

        .language-ruby.highlighter-rouge::before,
        .language-python.highlighter-rouge::before {
          content: "script";
        }

        .highlighter-rouge pre,
        .highlight pre {
          margin: 0;
          padding: 1rem 1.2rem 1.2rem;
          overflow-x: auto;
          background: transparent;
        }

        .highlighter-rouge code,
        .highlight code {
          color: #dce7f8;
          background: transparent;
          font-family: var(--mono-font);
          font-size: 0.93rem;
          line-height: 1.7;
        }

        .highlight .c,
        .highlight .ch,
        .highlight .cm,
        .highlight .c1,
        .highlight .cs {
          color: #637777;
          font-style: italic;
        }

        .highlight .k,
        .highlight .kc,
        .highlight .kd,
        .highlight .kn,
        .highlight .kp,
        .highlight .kr {
          color: #f7768e;
        }

        .highlight .nb,
        .highlight .bp,
        .highlight .vc,
        .highlight .vg,
        .highlight .vi {
          color: #bb9af7;
        }

        .highlight .na,
        .highlight .nc,
        .highlight .nd,
        .highlight .ne,
        .highlight .nf,
        .highlight .fm,
        .highlight .nn {
          color: #7aa2f7;
        }

        .highlight .nt {
          color: #2ac3de;
        }

        .highlight .s,
        .highlight .sa,
        .highlight .sb,
        .highlight .sc,
        .highlight .sd,
        .highlight .s1,
        .highlight .s2,
        .highlight .se,
        .highlight .sh,
        .highlight .si,
        .highlight .sx {
          color: #9ece6a;
        }

        .highlight .m,
        .highlight .mb,
        .highlight .mf,
        .highlight .mh,
        .highlight .mi,
        .highlight .il,
        .highlight .mo {
          color: #ff9e64;
        }

        .highlight .o,
        .highlight .ow,
        .highlight .p {
          color: #89ddff;
        }

        .highlight .sr,
        .highlight .ss {
          color: #b4f9f8;
        }

        .highlight .err {
          background: rgba(247, 118, 142, 0.14);
          color: #f7768e;
        }

        .page-glow {
          position: fixed;
          z-index: -1;
          width: 34rem;
          height: 34rem;
          pointer-events: none;
          filter: blur(110px);
          opacity: 0.26;
        }

        .page-glow--top {
          top: -10rem;
          right: -8rem;
          background: rgba(56, 189, 248, 0.28);
        }

        .page-glow--bottom {
          bottom: -12rem;
          left: -10rem;
          background: rgba(244, 114, 182, 0.2);
        }

        .site-footer {
          padding-bottom: 3rem;
          font-family: var(--ui-font);
          font-size: 0.9rem;
        }

        .empty-state {
          padding: 1rem 0 4rem;
          color: var(--muted);
        }

        @media (max-width: 720px) {
          body {
            font-size: 17px;
            line-height: 1.7;
          }

          .site-header {
            padding-top: 1.2rem;
          }

          .brand {
            width: 100%;
            border-radius: 1.3rem;
          }

          .hero,
          .post-shell {
            padding-top: 1.25rem;
          }

          .post-grid {
            grid-template-columns: 1fr;
          }

          .post-content {
            padding: 1rem 1rem 1.6rem;
            border-radius: 22px;
          }

          .highlighter-rouge pre,
          .highlight pre {
            padding: 0.92rem 1rem 1rem;
          }
        }
        """;
}
