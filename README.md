# mdpost

CLI tool for uploading markdown files to anonymous paste services and managing them as a local library with tags and full-text search. Includes a Terminal.Gui TUI dashboard.

## Why

Paste services are great for quick sharing, but they're fire-and-forget -- no way to find your old uploads, no tagging, no search. Meanwhile, services tied to real accounts (GitHub Gists, Google Docs) risk automated content filters locking your account over false positives. mdpost splits the difference: anonymous paste backends for sharing, local SQLite index for organization.

## Install

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/FuzzySlipper/md-post.git
cd md-post
dotnet build
```

Run directly:
```bash
dotnet run --project src/MdPost -- help
```

Or install as a global tool (optional):
```bash
dotnet pack -o nupkg
dotnet tool install --global --add-source nupkg mdpost
```

## Usage

### Upload a file

```bash
# Upload to your saved default backend (rentry initially), prints URL to stdout
mdpost upload analysis.md --title "SCS Analysis" --tags "geopolitics,south-china-sea"

# Upload from stdin
echo "# Quick note" | mdpost upload --stdin --title "Quick Note" --tags "misc"

# Use a different backend just for this upload
mdpost upload doc.md --backend paste.rs

# Save locally only (no upload)
mdpost upload doc.md --local --tags "draft"

# Change the default backend for future uploads
mdpost default-backend blog
```

### List and search

```bash
# List all posts
mdpost list

# Filter by tag
mdpost list --tag geopolitics

# Full-text search (FTS5 with stemming)
mdpost search "south china sea"

# Get URL for scripting
mdpost url my-post-slug-260405
```

### TUI dashboard

```bash
mdpost
```

Launches a Terminal.Gui dashboard with:

- **Left panel**: Tag list -- click to filter posts by tag
- **Main panel**: Post list sorted by most recent
- **Enter** on a post to view details, copy URL, upload, or re-upload

Keyboard shortcuts:

| Key | Action |
|-----|--------|
| U | Upload new post |
| S | Search |
| C | Copy URL of selected post |
| Del | Delete selected post |
| T | Clear tag filter |
| R | Refresh |
| Tab | Switch panels |
| Ctrl+Q | Quit |

### Personal blog backend

```bash
# Point mdpost at your GitHub Pages blog repo
mdpost blog-init --repo ~/.md-post/blog --url https://fuzzyslipper.github.io/blog

# Make blog publishing the default target
mdpost default-backend blog

# Install the bundled dark theme with improved markdown/code styling
mdpost blog-theme
```

## Backends

| Backend | Markdown rendering | Content filtering | Edit/Delete | Notes |
|---------|-------------------|-------------------|-------------|-------|
| **rentry.co** (default) | Excellent | Minimal (human moderation) | Yes (via edit codes) | Best rendering |
| **paste.rs** | Yes (append .md to URL) | None documented | No | Simplest |
| **blog** | Jekyll / GitHub Pages | Your repo, your rules | Yes (via git overwrite/delete) | Works well with `mdpost blog-theme` |
| **cf-blog** | Jekyll / Cloudflare Pages | Your repo, your rules | Yes (via git overwrite/delete) | Same post format, different host |

The paste backends stay anonymous. The blog backends publish into a repo you control instead.

## Storage

Posts are stored locally in `~/.md-post/mdpost.db` (SQLite with WAL mode). Each post tracks:

- Title, slug, content, tags
- Remote URL, edit code, backend name
- Source file path (if uploaded from a file)
- Timestamps

Full-text search uses SQLite FTS5 with Porter stemming across title, content, and tags.

## Project structure

```
src/MdPost/
  Program.cs                    Entry point + CLI command routing
  Data/
    DatabaseInitializer.cs      Schema, FTS5 triggers, WAL mode
    DbConnectionFactory.cs      Connection helper
    PostRepository.cs           CRUD, tag filtering, FTS5 search
  Models/
    Post.cs                     Post, PostSummary, PostSearchResult
  Services/
    IPasteService.cs            Backend interface
    RentryService.cs            rentry.co integration
    PasteRsService.cs           paste.rs integration
  Tui/
    DashboardView.cs            Terminal.Gui dashboard
```
