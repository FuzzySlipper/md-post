using MdPost.Data;
using MdPost.Models;
using MdPost.Services;
using Terminal.Gui;

namespace MdPost.Tui;

internal sealed class DashboardView : Toplevel
{
    private readonly PostRepository _repo;
    private readonly IPasteService[] _pasteServices;
    private readonly FrameView _tagFrame;
    private readonly ListView _tagList;
    private readonly FrameView _postFrame;
    private readonly ListView _postList;
    private readonly StatusBar _statusBar;

    private List<PostSummary> _posts = [];
    private List<string> _allTags = [];
    private string? _activeTag;
    private bool _refreshing;

    public DashboardView(PostRepository repo, IPasteService[] pasteServices)
    {
        _repo = repo;
        _pasteServices = pasteServices;

        // Tag sidebar (left, 20 wide)
        _tagList = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false
        };
        _tagList.OpenSelectedItem += OnTagActivated;

        _tagFrame = new FrameView("Tags")
        {
            X = 0, Y = 0,
            Width = 22,
            Height = Dim.Fill(1)
        };
        _tagFrame.Add(_tagList);

        // Post list (main area)
        _postList = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false
        };
        _postList.OpenSelectedItem += OnPostActivated;

        _postFrame = new FrameView("Posts")
        {
            X = 22, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };
        _postFrame.Add(_postList);

        // Status bar
        _statusBar = new StatusBar(new StatusItem[]
        {
            new(Key.Q | Key.CtrlMask, "~^Q~ Quit", () => Application.RequestStop()),
            new(Key.U, "~U~ Upload", () => ShowUploadDialog()),
            new(Key.S, "~S~ Search", () => ShowSearchDialog()),
            new(Key.R, "~R~ Refresh", () => _ = RefreshData()),
            new(Key.C, "~C~ Copy URL", OnCopyUrl),
            new(Key.Delete, "~Del~ Delete", () => OnDeletePost()),
            new(Key.T, "~T~ Clear Tag", ClearTagFilter),
            new(Key.Tab, "~Tab~ Switch", CycleFocus)
        });

        Add(_tagFrame, _postFrame, _statusBar);
    }

    public async Task StartAsync()
    {
        await RefreshData();
    }

    private async Task RefreshData()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            _allTags = await _repo.ListTagsAsync();
            var tagDisplay = new List<string> { "(all)" };
            tagDisplay.AddRange(_allTags);
            _tagList.SetSource(tagDisplay);

            if (_activeTag is not null)
            {
                var idx = _allTags.IndexOf(_activeTag);
                if (idx >= 0) _tagList.SelectedItem = idx + 1; // +1 for "(all)"
            }

            var filterTags = _activeTag is not null ? new[] { _activeTag } : null;
            _posts = await _repo.ListAsync(filterTags);

            var postLines = _posts.Select(FormatPostLine).ToList();
            _postList.SetSource(postLines.Count > 0 ? postLines : new List<string> { " (no posts)" });

            var filterLabel = _activeTag is not null ? $" [{_activeTag}]" : "";
            _postFrame.Title = $"Posts ({_posts.Count}){filterLabel}";
        }
        catch { _postFrame.Title = "Posts (error)"; }
        finally { _refreshing = false; }
    }

    private static string FormatPostLine(PostSummary p)
    {
        var time = FormatShortTime(p.UpdatedAt);
        var backend = p.Backend is not null ? $"[{p.Backend}]" : "[local]";
        var tags = p.Tags is { Count: > 0 } ? $" ({string.Join(",", p.Tags)})" : "";
        return $"[{time}] {backend,-10} {Truncate(p.Title, 40)}{tags}";
    }

    private void OnTagActivated(ListViewItemEventArgs args)
    {
        if (args.Item == 0)
        {
            _activeTag = null;
        }
        else
        {
            var tagIdx = args.Item - 1;
            if (tagIdx >= 0 && tagIdx < _allTags.Count)
                _activeTag = _allTags[tagIdx];
        }
        _ = RefreshData();
    }

    private void ClearTagFilter()
    {
        _activeTag = null;
        _ = RefreshData();
    }

    private void OnPostActivated(ListViewItemEventArgs args)
    {
        if (args.Item < 0 || args.Item >= _posts.Count) return;
        ShowPostDetail(_posts[args.Item]);
    }

    private async void ShowPostDetail(PostSummary summary)
    {
        Post? post;
        try { post = await _repo.GetByIdAsync(summary.Id); }
        catch { return; }
        if (post is null) return;

        var dlg = new Dialog($"Post: {Truncate(post.Title, 55)}", 80, 30);

        var lines = new List<string>
        {
            post.Title,
            new string('─', Math.Min(post.Title.Length, 76)),
            "",
            $"  Slug:    {post.Slug}",
            $"  Backend: {post.Backend ?? "local only"}",
            $"  URL:     {post.RemoteUrl ?? "(not uploaded)"}",
            $"  Created: {post.CreatedAt:yyyy-MM-dd HH:mm}",
            $"  Updated: {post.UpdatedAt:yyyy-MM-dd HH:mm}"
        };

        if (post.Tags is { Count: > 0 })
            lines.Add($"  Tags:    {string.Join(", ", post.Tags)}");

        if (post.SourcePath is not null)
            lines.Add($"  Source:  {post.SourcePath}");

        lines.Add("");
        lines.Add("  ──────────────────────────────────────");
        foreach (var line in post.Content.Split('\n'))
            lines.Add($"  {line}");

        var textView = new TextView
        {
            X = 1, Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            ReadOnly = true,
            WordWrap = true,
            Text = string.Join("\n", lines)
        };
        dlg.Add(textView);

        if (post.RemoteUrl is not null)
        {
            var copyBtn = new Button("Copy URL");
            copyBtn.Clicked += () =>
            {
                CopyToClipboard(post.RemoteUrl);
                MessageBox.Query("Copied", post.RemoteUrl, "OK");
            };
            dlg.AddButton(copyBtn);
        }

        if (post.RemoteUrl is null)
        {
            var uploadBtn = new Button("Upload");
            uploadBtn.Clicked += () =>
            {
                Application.RequestStop();
                _ = UploadExistingPost(post);
            };
            dlg.AddButton(uploadBtn);
        }

        var reuploadBtn = new Button("Re-upload");
        reuploadBtn.Clicked += () =>
        {
            Application.RequestStop();
            _ = UploadExistingPost(post);
        };
        dlg.AddButton(reuploadBtn);

        var close = new Button("Close") { IsDefault = true };
        close.Clicked += () => Application.RequestStop();
        dlg.AddButton(close);

        Application.Run(dlg);
    }

    private async Task UploadExistingPost(Post post)
    {
        var service = _pasteServices[0]; // Default to first service
        try
        {
            var result = await service.UploadAsync(post.Content);
            await _repo.UpdateRemoteAsync(post.Slug, result.Url, result.EditCode, result.Backend);
            CopyToClipboard(result.Url);
            MessageBox.Query("Uploaded", $"URL copied to clipboard:\n{result.Url}", "OK");
            await RefreshData();
        }
        catch (Exception ex)
        {
            MessageBox.Query("Error", $"Upload failed: {ex.Message}", "OK");
        }
    }

    private void ShowUploadDialog()
    {
        var dlg = new Dialog("Upload Post", 70, 18);

        var titleLabel = new Label("Title:") { X = 1, Y = 1 };
        var titleField = new TextField("") { X = 12, Y = 1, Width = Dim.Fill(2) };

        var tagsLabel = new Label("Tags:") { X = 1, Y = 3 };
        var tagsField = new TextField("") { X = 12, Y = 3, Width = Dim.Fill(2) };
        var tagsHint = new Label("(comma separated)") { X = 12, Y = 4, ColorScheme = Colors.Base };

        var sourceLabel = new Label("Source:") { X = 1, Y = 6 };
        var sourceField = new TextField("") { X = 12, Y = 6, Width = Dim.Fill(2) };
        var sourceHint = new Label("File path, or leave empty to type content") { X = 12, Y = 7, ColorScheme = Colors.Base };

        var backendLabel = new Label("Backend:") { X = 1, Y = 9 };
        var backendNames = _pasteServices.Select(s => s.Name).Append("local only").ToArray();
        var backendRadio = new RadioGroup(backendNames.Select(n => NStack.ustring.Make(n)).ToArray())
        {
            X = 12, Y = 9,
            DisplayMode = DisplayModeLayout.Horizontal
        };

        dlg.Add(titleLabel, titleField, tagsLabel, tagsField, tagsHint,
                sourceLabel, sourceField, sourceHint, backendLabel, backendRadio);

        var okBtn = new Button("Upload");
        okBtn.Clicked += () =>
        {
            var title = titleField.Text?.ToString()?.Trim();
            if (string.IsNullOrEmpty(title))
            {
                MessageBox.Query("Error", "Title is required.", "OK");
                return;
            }

            Application.RequestStop();
            _ = DoUpload(
                title,
                tagsField.Text?.ToString()?.Trim(),
                sourceField.Text?.ToString()?.Trim(),
                backendRadio.SelectedItem);
        };
        dlg.AddButton(okBtn);

        var cancelBtn = new Button("Cancel");
        cancelBtn.Clicked += () => Application.RequestStop();
        dlg.AddButton(cancelBtn);

        Application.Run(dlg);
    }

    private async Task DoUpload(string title, string? tagsStr, string? sourcePath, int backendIdx)
    {
        string content;
        string? resolvedSourcePath = null;

        if (!string.IsNullOrEmpty(sourcePath))
        {
            var fullPath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullPath))
            {
                MessageBox.Query("Error", $"File not found: {fullPath}", "OK");
                return;
            }
            content = await File.ReadAllTextAsync(fullPath);
            resolvedSourcePath = fullPath;
        }
        else
        {
            // Show text input dialog
            content = ShowContentEditor();
            if (string.IsNullOrEmpty(content)) return;
        }

        var tags = string.IsNullOrEmpty(tagsStr)
            ? null
            : tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .ToList();

        var slug = GenerateSlug(title);

        var post = new Post
        {
            Slug = slug,
            Title = title,
            Content = content,
            Tags = tags,
            SourcePath = resolvedSourcePath
        };

        // Upload to remote if a paste service was selected
        var isLocalOnly = backendIdx >= _pasteServices.Length;
        if (!isLocalOnly)
        {
            var service = _pasteServices[backendIdx];
            try
            {
                var uploadContent = content;
                if (service.Name is "blog" or "cf-blog" && !content.TrimStart().StartsWith("---"))
                {
                    var fmTags = tags is { Count: > 0 } ? $"\ntags: [{string.Join(", ", tags)}]" : "";
                    uploadContent = $"---\ntitle: \"{title}\"{fmTags}\n---\n\n{content}";
                }
                var result = await service.UploadAsync(uploadContent, slug);
                post.RemoteUrl = result.Url;
                post.EditCode = result.EditCode;
                post.Backend = result.Backend;
                CopyToClipboard(result.Url);
            }
            catch (Exception ex)
            {
                MessageBox.Query("Error", $"Upload failed: {ex.Message}\nSaving locally.", "OK");
                // ErrorBox returns 0, so always save locally on error
            }
        }

        await _repo.UpsertAsync(post);

        var msg = post.RemoteUrl is not null
            ? $"Saved and uploaded.\nURL copied to clipboard:\n{post.RemoteUrl}"
            : "Saved locally.";
        MessageBox.Query("Done", msg, "OK");

        await RefreshData();
    }

    private static string ShowContentEditor()
    {
        var dlg = new Dialog("Enter Content (Esc to cancel, ^S to save)", 80, 25);
        var textView = new TextView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            ReadOnly = false,
            WordWrap = true
        };
        dlg.Add(textView);

        var content = "";

        var saveBtn = new Button("Save");
        saveBtn.Clicked += () =>
        {
            content = textView.Text?.ToString() ?? "";
            Application.RequestStop();
        };
        dlg.AddButton(saveBtn);

        var cancelBtn = new Button("Cancel");
        cancelBtn.Clicked += () => Application.RequestStop();
        dlg.AddButton(cancelBtn);

        Application.Run(dlg);
        return content;
    }

    private void ShowSearchDialog()
    {
        var dlg = new Dialog("Search Posts", 60, 7);

        var queryLabel = new Label("Query:") { X = 1, Y = 1 };
        var queryField = new TextField("") { X = 10, Y = 1, Width = Dim.Fill(2) };
        dlg.Add(queryLabel, queryField);

        var okBtn = new Button("Search");
        okBtn.Clicked += () =>
        {
            var query = queryField.Text?.ToString()?.Trim();
            if (string.IsNullOrEmpty(query)) return;
            Application.RequestStop();
            _ = ShowSearchResults(query);
        };
        dlg.AddButton(okBtn);

        var cancelBtn = new Button("Cancel");
        cancelBtn.Clicked += () => Application.RequestStop();
        dlg.AddButton(cancelBtn);

        Application.Run(dlg);
    }

    private async Task ShowSearchResults(string query)
    {
        List<PostSearchResult> results;
        try { results = await _repo.SearchAsync(query); }
        catch
        {
            MessageBox.Query("Error", "Search failed.", "OK");
            return;
        }

        if (results.Count == 0)
        {
            MessageBox.Query("Search", "No results found.", "OK");
            return;
        }

        var dlg = new Dialog($"Results for \"{Truncate(query, 30)}\" ({results.Count})", 78, 22);
        var lines = results.Select(r =>
        {
            var snippet = r.Snippet.Replace("<b>", "").Replace("</b>", "");
            return $"{r.Title}\n  {Truncate(snippet.ReplaceLineEndings(" "), 65)}";
        }).ToList();

        var list = new ListView(lines)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };
        list.OpenSelectedItem += (e) =>
        {
            if (e.Item >= 0 && e.Item < results.Count)
            {
                Application.RequestStop();
                var summary = new PostSummary
                {
                    Id = results[e.Item].Id,
                    Slug = results[e.Item].Slug,
                    Title = results[e.Item].Title,
                    RemoteUrl = results[e.Item].RemoteUrl,
                    UpdatedAt = DateTime.MinValue
                };
                ShowPostDetail(summary);
            }
        };
        dlg.Add(list);

        var close = new Button("Close") { IsDefault = true };
        close.Clicked += () => Application.RequestStop();
        dlg.AddButton(close);

        Application.Run(dlg);
    }

    private async void OnDeletePost()
    {
        if (_postList.SelectedItem < 0 || _postList.SelectedItem >= _posts.Count) return;
        var post = _posts[_postList.SelectedItem];

        var confirm = MessageBox.Query("Delete", $"Delete \"{Truncate(post.Title, 40)}\"?", "Yes", "No");
        if (confirm != 0) return;

        try
        {
            // Try remote delete if we have an edit code
            if (post is { Backend: not null })
            {
                var fullPost = await _repo.GetByIdAsync(post.Id);
                if (fullPost?.EditCode is not null)
                {
                    var service = _pasteServices.FirstOrDefault(s => s.Name == fullPost.Backend);
                    if (service is not null)
                        await service.DeleteAsync(fullPost.EditCode, fullPost.RemoteUrl!);
                }
            }

            await _repo.DeleteAsync(post.Slug);
            await RefreshData();
        }
        catch (Exception ex)
        {
            MessageBox.Query("Error", $"Delete failed: {ex.Message}", "OK");
        }
    }

    private void OnCopyUrl()
    {
        if (_postList.SelectedItem < 0 || _postList.SelectedItem >= _posts.Count) return;
        var post = _posts[_postList.SelectedItem];
        if (post.RemoteUrl is null)
        {
            MessageBox.Query("No URL", "This post hasn't been uploaded yet.", "OK");
            return;
        }
        CopyToClipboard(post.RemoteUrl);
        MessageBox.Query("Copied", post.RemoteUrl, "OK");
    }

    public override bool ProcessKey(KeyEvent kb)
    {
        if (!kb.IsAlt && !kb.IsCtrl)
        {
            switch (char.ToLower((char)kb.Key))
            {
                case 'u': ShowUploadDialog(); return true;
                case 's': ShowSearchDialog(); return true;
                case 'r': _ = RefreshData(); return true;
                case 'c': OnCopyUrl(); return true;
                case 't': ClearTagFilter(); return true;
            }
        }
        return base.ProcessKey(kb);
    }

    private void CycleFocus()
    {
        if (_tagFrame.HasFocus)
            _postFrame.SetFocus();
        else
            _tagFrame.SetFocus();
    }

    private static void CopyToClipboard(string text)
    {
        // Try common clipboard tools
        string[] tools = ["xclip -selection clipboard", "xsel --clipboard --input", "wl-copy"];
        foreach (var tool in tools)
        {
            try
            {
                var parts = tool.Split(' ', 2);
                var psi = new System.Diagnostics.ProcessStartInfo(parts[0])
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (parts.Length > 1)
                    psi.Arguments = parts[1];

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc is null) continue;
                proc.StandardInput.Write(text);
                proc.StandardInput.Close();
                proc.WaitForExit(2000);
                if (proc.ExitCode == 0) return;
            }
            catch { /* try next tool */ }
        }
    }

    private static string GenerateSlug(string title)
    {
        var slug = title.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace("_", "-");
        // Strip non-alphanumeric except hyphens
        slug = new string(slug.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        // Collapse multiple hyphens
        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (slug.Length > 60)
            slug = slug[..60].TrimEnd('-');
        // Add short timestamp for uniqueness
        slug += $"-{DateTime.UtcNow:yyMMdd}";
        return slug;
    }

    private static string FormatShortTime(DateTime dt)
    {
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h";
        return $"{(int)diff.TotalDays}d";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";
}
