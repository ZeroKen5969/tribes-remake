using System;
using System.IO;

namespace Tribes.Core.Tests;

internal static class TestPaths
{
    /// <summary>The repository root: the first directory above the tests with data/rules and Tribes.sln (the original repository, when cloned, is original/).</summary>
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "rules"))
                && File.Exists(Path.Combine(dir.FullName, "Tribes.sln")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("repository root not found above " + AppContext.BaseDirectory);
    }
}
