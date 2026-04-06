using System.Text.Json;
using MdPost.Models;
using Microsoft.Data.Sqlite;

namespace MdPost.Data;

public sealed class PostRepository
{
    private readonly DbConnectionFactory _db;

    public PostRepository(DbConnectionFactory db) => _db = db;

    public async Task<Post> UpsertAsync(Post post)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO posts (slug, title, content, tags, remote_url, edit_code, backend, source_path)
            VALUES (@slug, @title, @content, @tags, @remoteUrl, @editCode, @backend, @sourcePath)
            ON CONFLICT(slug) DO UPDATE SET
                title = excluded.title,
                content = excluded.content,
                tags = excluded.tags,
                remote_url = excluded.remote_url,
                edit_code = excluded.edit_code,
                backend = excluded.backend,
                source_path = excluded.source_path,
                updated_at = datetime('now')
            RETURNING id, slug, title, content, tags, remote_url, edit_code, backend, source_path, created_at, updated_at
            """;
        cmd.Parameters.AddWithValue("@slug", post.Slug);
        cmd.Parameters.AddWithValue("@title", post.Title);
        cmd.Parameters.AddWithValue("@content", post.Content);
        cmd.Parameters.AddWithValue("@tags",
            post.Tags is { Count: > 0 } ? JsonSerializer.Serialize(post.Tags) : DBNull.Value);
        cmd.Parameters.AddWithValue("@remoteUrl", (object?)post.RemoteUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@editCode", (object?)post.EditCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@backend", (object?)post.Backend ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sourcePath", (object?)post.SourcePath ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return ReadPost(reader);
    }

    public async Task<Post?> GetAsync(string slug)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, slug, title, content, tags, remote_url, edit_code, backend, source_path, created_at, updated_at
            FROM posts WHERE slug = @slug
            """;
        cmd.Parameters.AddWithValue("@slug", slug);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPost(reader) : null;
    }

    public async Task<Post?> GetByIdAsync(int id)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, slug, title, content, tags, remote_url, edit_code, backend, source_path, created_at, updated_at
            FROM posts WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPost(reader) : null;
    }

    public async Task<List<PostSummary>> ListAsync(string[]? tags = null)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();

        var where = new List<string>();

        if (tags is { Length: > 0 })
        {
            for (var i = 0; i < tags.Length; i++)
            {
                var p = $"@tag{i}";
                where.Add($"EXISTS (SELECT 1 FROM json_each(tags) WHERE json_each.value = {p})");
                cmd.Parameters.AddWithValue(p, tags[i]);
            }
        }

        var whereClause = where.Count > 0 ? $"WHERE {string.Join(" AND ", where)}" : "";
        cmd.CommandText = $"""
            SELECT id, slug, title, tags, remote_url, backend, updated_at
            FROM posts {whereClause}
            ORDER BY updated_at DESC
            """;

        var results = new List<PostSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var tagsJson = reader.IsDBNull(3) ? null : reader.GetString(3);
            results.Add(new PostSummary
            {
                Id = reader.GetInt32(0),
                Slug = reader.GetString(1),
                Title = reader.GetString(2),
                Tags = tagsJson is not null ? JsonSerializer.Deserialize<List<string>>(tagsJson) : null,
                RemoteUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                Backend = reader.IsDBNull(5) ? null : reader.GetString(5),
                UpdatedAt = DateTime.Parse(reader.GetString(6))
            });
        }
        return results;
    }

    public async Task<List<string>> ListTagsAsync()
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT j.value
            FROM posts, json_each(posts.tags) j
            ORDER BY j.value
            """;

        var tags = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tags.Add(reader.GetString(0));
        return tags;
    }

    public async Task<List<PostSearchResult>> SearchAsync(string query)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.slug, p.title, p.remote_url,
                   snippet(posts_fts, 1, '<b>', '</b>', '...', 32) as snippet,
                   rank
            FROM posts_fts fts
            JOIN posts p ON p.id = fts.rowid
            WHERE posts_fts MATCH @query
            ORDER BY rank
            """;
        cmd.Parameters.AddWithValue("@query", query);

        var results = new List<PostSearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new PostSearchResult
            {
                Id = reader.GetInt32(0),
                Slug = reader.GetString(1),
                Title = reader.GetString(2),
                RemoteUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
                Snippet = reader.GetString(4),
                Rank = reader.GetDouble(5)
            });
        }
        return results;
    }

    public async Task<bool> DeleteAsync(string slug)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM posts WHERE slug = @slug";
        cmd.Parameters.AddWithValue("@slug", slug);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task UpdateRemoteAsync(string slug, string remoteUrl, string? editCode, string backend)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE posts SET remote_url = @remoteUrl, edit_code = @editCode, backend = @backend, updated_at = datetime('now')
            WHERE slug = @slug
            """;
        cmd.Parameters.AddWithValue("@slug", slug);
        cmd.Parameters.AddWithValue("@remoteUrl", remoteUrl);
        cmd.Parameters.AddWithValue("@editCode", (object?)editCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@backend", backend);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Post ReadPost(SqliteDataReader reader)
    {
        var tagsJson = reader.IsDBNull(4) ? null : reader.GetString(4);
        return new Post
        {
            Id = reader.GetInt32(0),
            Slug = reader.GetString(1),
            Title = reader.GetString(2),
            Content = reader.GetString(3),
            Tags = tagsJson is not null ? JsonSerializer.Deserialize<List<string>>(tagsJson) : null,
            RemoteUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
            EditCode = reader.IsDBNull(6) ? null : reader.GetString(6),
            Backend = reader.IsDBNull(7) ? null : reader.GetString(7),
            SourcePath = reader.IsDBNull(8) ? null : reader.GetString(8),
            CreatedAt = DateTime.Parse(reader.GetString(9)),
            UpdatedAt = DateTime.Parse(reader.GetString(10))
        };
    }
}
