namespace Mfop.Api.Tests;

internal static class RepoPaths
{
    public static string Path(params string[] parts) => System.IO.Path.Combine([FindRoot(), .. parts]);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (AGENTS.md) not found.");
    }
}
