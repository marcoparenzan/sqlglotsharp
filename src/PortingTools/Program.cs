using System.Diagnostics;

// Development-only bridge for invoking the Python tooling through the available dotnet CLI.
if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project src/PortingTools -- <python arguments>");
    return 2;
}
var executable = Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python";
var start = new ProcessStartInfo(executable) { UseShellExecute = false };
foreach (var argument in args) start.ArgumentList.Add(argument);
start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
start.Environment["TMP"] = Path.Combine(FindRepoRoot(), "artifacts", "tmp");
start.Environment["TEMP"] = start.Environment["TMP"];
Directory.CreateDirectory(start.Environment["TMP"]!);
static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "UPSTREAM.json"))) return dir.FullName;
    return Directory.GetCurrentDirectory();
}
try
{
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start Python.");
    await process.WaitForExitAsync();
    return process.ExitCode;
}
catch (System.ComponentModel.Win32Exception error)
{
    Console.Error.WriteLine($"Cannot start {executable}: {error.Message}. Set SQLGLOT_PYTHON to a Python 3.11+ executable.");
    return 2;
}
