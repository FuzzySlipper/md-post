namespace MdPost.Services;

public static class UploadContentBuilder
{
    public static string Prepare(string backendName, string title, IReadOnlyCollection<string>? tags, string content)
    {
        if (backendName is not "blog" and not "cf-blog")
            return content;

        if (content.TrimStart().StartsWith("---", StringComparison.Ordinal))
            return content;

        var tagsLine = tags is { Count: > 0 }
            ? $"\ntags: [{string.Join(", ", tags)}]"
            : "";

        return $"---\ntitle: \"{EscapeYaml(title)}\"{tagsLine}\n---\n\n{content}";
    }

    private static string EscapeYaml(string value) => value.Replace("\"", "\\\"");
}
