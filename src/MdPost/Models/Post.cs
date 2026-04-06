namespace MdPost.Models;

public sealed class Post
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public List<string>? Tags { get; set; }
    public string? RemoteUrl { get; set; }
    public string? EditCode { get; set; }
    public string? Backend { get; set; }
    public string? SourcePath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class PostSummary
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Title { get; set; }
    public List<string>? Tags { get; set; }
    public string? RemoteUrl { get; set; }
    public string? Backend { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class PostSearchResult
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Title { get; set; }
    public string? RemoteUrl { get; set; }
    public required string Snippet { get; set; }
    public double Rank { get; set; }
}
