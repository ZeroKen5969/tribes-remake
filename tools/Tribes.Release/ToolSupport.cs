using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Tribes.Release;

/// <summary>A failure reported to the user as one line, without a stack trace.</summary>
internal sealed class ToolException : Exception
{
    public ToolException(string message)
        : base(message)
    {
    }

    public ToolException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>Output that never shows a secret: every registered secret is replaced by ***.</summary>
internal static class Out
{
    private static readonly List<string> Secrets = [];

    public static void RegisterSecret(string secret)
    {
        if (secret.Length >= 8)
        {
            lock (Secrets)
            {
                Secrets.Add(secret);
            }
        }
    }

    public static string Redact(string text)
    {
        lock (Secrets)
        {
            return Secrets.Aggregate(text, (current, secret) => current.Replace(secret, "***", StringComparison.Ordinal));
        }
    }

    public static void Line(string text = "") => Console.WriteLine(Redact(text));

    public static void Error(string text) => Console.Error.WriteLine(Redact(text));
}

/// <summary>The command line: a command, then <c>--name value</c> options and <c>--flag</c>s.</summary>
internal sealed class Options
{
    private static readonly HashSet<string> FlagNames = ["dry-run", "prerelease", "yes", "allow-stable", "keep", "force"];
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    private readonly HashSet<string> flags = new(StringComparer.Ordinal);

    public Options(IReadOnlyList<string> args)
    {
        Command = args.Count > 0 ? args[0] : "";
        var positional = new List<string>();
        for (int i = 1; i < args.Count; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            string name = arg[2..];
            if (FlagNames.Contains(name))
            {
                flags.Add(name);
            }
            else if (i + 1 < args.Count)
            {
                values[name] = args[++i];
            }
            else
            {
                throw new ToolException($"--{name} needs a value.");
            }
        }

        Positional = positional;
    }

    public string Command { get; }

    public IReadOnlyList<string> Positional { get; }

    public string? Get(string name) => values.GetValueOrDefault(name);

    public string Require(string name) => Get(name) ?? throw new ToolException($"--{name} is required.");

    public bool Flag(string name) => flags.Contains(name);

    public int GetInt(string name, int fallback) =>
        Get(name) is string text
            ? (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : throw new ToolException($"--{name} must be a whole number."))
            : fallback;
}

/// <summary>Repository paths and the secrets file.</summary>
internal static class RepoPaths
{
    /// <summary>The directory with Tribes.sln, found from the current directory or the tool's location.</summary>
    public static string Root(Options options)
    {
        if (options.Get("repo-root") is string explicitRoot)
        {
            return Path.GetFullPath(explicitRoot);
        }

        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Tribes.sln")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new ToolException("Cannot find the repository root (Tribes.sln); pass --repo-root.");
    }

    public static string SigningKey(Options options) =>
        Path.GetFullPath(options.Get("key") ?? Path.Combine(Root(options), "secrets", "update-signing-key.pem"));

    public static string TrustedKeys(Options options) =>
        Path.GetFullPath(options.Get("keys-file") ?? Path.Combine(Root(options), "src", "Tribes.Update", "UpdateKeys.json"));

    public static string SecretsFile(Options options) =>
        Path.GetFullPath(options.Get("secrets") ?? Path.Combine(Root(options), "secrets", "github.json"));
}

/// <summary>
/// secrets/github.json: <c>repo</c>, <c>writeToken</c> (creates releases), <c>readToken</c> (what the
/// game embeds). The tokens are registered with <see cref="Out"/> so they never get printed.
/// </summary>
internal sealed class GitHubSecrets
{
    private GitHubSecrets(string repository, string? writeToken, string? readToken)
    {
        Repository = repository;
        WriteToken = writeToken;
        ReadToken = readToken;
    }

    public string Repository { get; }

    public string? WriteToken { get; }

    public string? ReadToken { get; }

    public static GitHubSecrets Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new ToolException($"The secrets file {path} does not exist.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            string repo = Text(root, "repo") ?? throw new ToolException("The secrets file has no \"repo\".");
            string? write = Text(root, "writeToken");
            string? read = Text(root, "readToken");
            foreach (string? token in new[] { write, read })
            {
                if (token is not null)
                {
                    Out.RegisterSecret(token);
                }
            }

            return new GitHubSecrets(repo, write, read);
        }
        catch (JsonException)
        {
            throw new ToolException("The secrets file is not valid JSON.");
        }
    }

    public string RequireWriteToken() => WriteToken ?? throw new ToolException("The secrets file has no \"writeToken\".");

    public string RequireReadToken() => ReadToken ?? throw new ToolException("The secrets file has no \"readToken\".");

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 0
            ? value.GetString()
            : null;
}
