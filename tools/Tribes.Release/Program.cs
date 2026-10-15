using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tribes.Update;

namespace Tribes.Release;

/// <summary>
/// Release tool for the in-game updater (docs/UPDATES.md):
/// keygen, trust, publish, verify, check, delete.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage: dotnet run --project tools/Tribes.Release -- <command> [options]

          keygen                       Create secrets/update-signing-key.pem if missing and trust its public key
          trust list | remove <keyId>  Show or edit the keys the game trusts (src/Tribes.Update/UpdateKeys.json)
          publish --version X.Y.Z [--content-rev N] [--min-app-version V]
                  [--apk FILE] [--windows-zip FILE] [--pck FILE] --notes FILE
                  [--prerelease] [--title TEXT] [--dry-run] [--out DIR]
                                       Sign the manifest and create the GitHub release
          verify --tag vX.Y.Z[-rN] [--download-dir DIR] [--keep]
                                       Download and check a release with the read token, as the game does
          check --current X.Y.Z --platform android|windows [--content-rev N] [--prerelease]
                                       Show the update the game would be offered
          delete --tag T --release-id ID [--yes] [--allow-stable]
                                       Delete one release and its tag

        Common: --repo-root DIR  --secrets FILE  --key FILE  --keys-file FILE
        """;

    public static async Task<int> Main(string[] args)
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        try
        {
            var options = new Options(args);
            return options.Command switch
            {
                "keygen" => SigningKeys.KeyGen(options),
                "trust" => SigningKeys.Trust(options),
                "publish" => await Publisher.RunAsync(options, cancel.Token),
                "verify" => await Verifier.VerifyAsync(options, cancel.Token),
                "check" => await Verifier.CheckAsync(options, cancel.Token),
                "delete" => await Verifier.DeleteAsync(options, cancel.Token),
                _ => PrintUsage(),
            };
        }
        catch (ToolException e)
        {
            Out.Error("Error: " + e.Message);
            return 1;
        }
        catch (UpdateException e)
        {
            Out.Error($"Error ({e.Kind}): {e.Message}");
            Out.Error("  " + e.UserMessage);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Out.Error("Cancelled.");
            return 2;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or ArgumentException or InvalidOperationException or System.Net.Http.HttpRequestException)
        {
            Out.Error($"Error ({e.GetType().Name}): {e.Message}");
            return 1;
        }
    }

    private static int PrintUsage()
    {
        Out.Line(Usage);
        return 1;
    }
}
