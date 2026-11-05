using System.Diagnostics;

namespace SqlGlotSharp.Tests;

public class ClosureToolTests
{
    [Theory]
    [InlineData("test_closure.py")]
    [InlineData("test_port_status.py")]
    [InlineData("test_gen_csharp.py")]
    [InlineData("test_extract_corpus.py")]
    [Trait("Level", "Tooling")]
    public async Task Python_tooling_unit_tests_pass(string scriptName)
    {
        // Build output is external; locate the repository from the MSBuild-provided path.
        var script = Path.Combine(RepositoryRoot, "src", "tools", scriptName);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add(script);
        var scratch = RepoPaths.Scratch;
        Directory.CreateDirectory(scratch);
        start.Environment["TEMP"] = scratch;
        start.Environment["TMP"] = scratch;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    private static string RepositoryRoot =>
        Path.GetFullPath(Path.Combine(GetSourceDirectory(), "..", ".."));

    private static string GetSourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetDirectoryName(source)!;
}
