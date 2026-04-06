using Microsoft.Data.Sqlite;

namespace MdPost.Data;

public sealed class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = $"Data Source={databasePath}";
    }

    public string ConnectionString => _connectionString;

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
        await pragmaCmd.ExecuteNonQueryAsync();

        await using var schemaCmd = connection.CreateCommand();
        schemaCmd.CommandText = Schema;
        await schemaCmd.ExecuteNonQueryAsync();
    }

    internal const string Schema = """
        CREATE TABLE IF NOT EXISTS posts (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            slug        TEXT NOT NULL UNIQUE,
            title       TEXT NOT NULL,
            content     TEXT NOT NULL,
            tags        TEXT,
            remote_url  TEXT,
            edit_code   TEXT,
            backend     TEXT,
            source_path TEXT,
            created_at  TEXT NOT NULL DEFAULT (datetime('now')),
            updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
        );

        CREATE INDEX IF NOT EXISTS idx_posts_updated ON posts(updated_at DESC);

        -- FTS5 for full-text search
        CREATE VIRTUAL TABLE IF NOT EXISTS posts_fts USING fts5(
            title,
            content,
            tags,
            content=posts,
            content_rowid=id,
            tokenize='porter unicode61'
        );

        -- Triggers to keep FTS in sync
        CREATE TRIGGER IF NOT EXISTS posts_ai AFTER INSERT ON posts BEGIN
            INSERT INTO posts_fts(rowid, title, content, tags)
            VALUES (new.id, new.title, new.content, new.tags);
        END;

        CREATE TRIGGER IF NOT EXISTS posts_ad AFTER DELETE ON posts BEGIN
            INSERT INTO posts_fts(posts_fts, rowid, title, content, tags)
            VALUES ('delete', old.id, old.title, old.content, old.tags);
        END;

        CREATE TRIGGER IF NOT EXISTS posts_au AFTER UPDATE ON posts BEGIN
            INSERT INTO posts_fts(posts_fts, rowid, title, content, tags)
            VALUES ('delete', old.id, old.title, old.content, old.tags);
            INSERT INTO posts_fts(rowid, title, content, tags)
            VALUES (new.id, new.title, new.content, new.tags);
        END;
        """;
}
