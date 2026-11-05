namespace SqlGlotSharp.Tests;

/// <summary>Repository-relative locations (tests run from artifacts/SqlGlotSharp.Tests/bin, inside the repository).</summary>
internal static class RepoPaths
{
    public static readonly string Root = FindRoot();
    public static readonly string Artifacts = Path.Combine(Root, "artifacts");
    // Scratch files (recorded corpus copies and the like) must stay outside the repository: tests assert that no corpus is copied into the workspace.
    public static readonly string Scratch = Path.Combine(Path.GetTempPath(), "sqlglot-tests");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "UPSTREAM.json"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }
}
