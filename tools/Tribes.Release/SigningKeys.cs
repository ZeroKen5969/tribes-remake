using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Tribes.Update;

namespace Tribes.Release;

/// <summary>
/// The manifest signing key (an ECDSA P-256 private key, PKCS#8 PEM, readable by the current user
/// only) and the list of public keys the game trusts (src/Tribes.Update/UpdateKeys.json).
/// </summary>
internal static class SigningKeys
{
    /// <summary>
    /// keygen: creates the key when missing (never overwrites one) and makes sure its public key is
    /// in the trusted list. Prints ids and paths only.
    /// </summary>
    public static int KeyGen(Options options)
    {
        string keyPath = RepoPaths.SigningKey(options);
        string trustedPath = RepoPaths.TrustedKeys(options);
        bool created = false;
        if (!File.Exists(keyPath))
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            WritePrivateKey(keyPath, key.ExportPkcs8PrivateKeyPem());
            created = true;
        }

        using ECDsa signing = Load(keyPath);
        TrustedKey trusted = TrustedKey.FromKey(signing);
        TrustedKeySet set = ReadTrusted(trustedPath);
        bool added = set.Find(trusted.KeyId) is null;
        if (added)
        {
            File.WriteAllBytes(trustedPath, set.With(trusted).Serialize());
        }

        Out.Line(created ? $"Created signing key {trusted.KeyId}: {keyPath}" : $"Signing key {trusted.KeyId} already exists: {keyPath}");
        Out.Line(added ? $"Added {trusted.KeyId} to the trusted keys: {trustedPath}" : $"{trusted.KeyId} is already trusted: {trustedPath}");
        if (added)
        {
            Out.Line("Rebuild the game so it trusts the new key before publishing with it.");
        }

        return 0;
    }

    /// <summary>trust list | trust remove &lt;id&gt;.</summary>
    public static int Trust(Options options)
    {
        string trustedPath = RepoPaths.TrustedKeys(options);
        TrustedKeySet set = ReadTrusted(trustedPath);
        string action = options.Positional.Count > 0 ? options.Positional[0] : "list";
        if (action == "list")
        {
            Out.Line(set.Keys.Count == 0 ? "No trusted keys." : string.Join(Environment.NewLine, set.Keys.Select(k => k.KeyId)));
            return 0;
        }

        if (action != "remove" || options.Positional.Count < 2)
        {
            throw new ToolException("Usage: trust list | trust remove <keyId>");
        }

        string id = options.Positional[1];
        if (set.Find(id) is null)
        {
            throw new ToolException($"{id} is not in the trusted keys.");
        }

        if (File.Exists(RepoPaths.SigningKey(options)) && !options.Flag("force"))
        {
            using ECDsa current = Load(RepoPaths.SigningKey(options));
            if (TrustedKey.FromKey(current).KeyId == id)
            {
                throw new ToolException($"{id} is the current signing key; pass --force to remove it anyway.");
            }
        }

        File.WriteAllBytes(trustedPath, set.Without(id).Serialize());
        Out.Line($"Removed {id}. Games built from now on reject manifests signed with it.");
        return 0;
    }

    public static ECDsa Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new ToolException($"The signing key {path} does not exist; run keygen.");
        }

        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(File.ReadAllText(path));
            try
            {
                _ = TrustedKey.FromKey(key);
            }
            catch (ArgumentException)
            {
                throw new ToolException("The signing key is not an ECDSA P-256 key.");
            }

            return key;
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            key.Dispose();
            throw new ToolException($"The signing key {path} cannot be read.");
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public static TrustedKeySet ReadTrusted(string path) =>
        File.Exists(path) ? TrustedKeySet.Parse(File.ReadAllBytes(path)) : new TrustedKeySet([]);

    /// <summary>Writes the PEM to a new file that only the current user can open.</summary>
    internal static void WritePrivateKey(string path, string pem)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (OperatingSystem.IsWindows())
        {
            using FileStream stream = new FileInfo(path).Create(
                FileMode.CreateNew,
                FileSystemRights.Write | FileSystemRights.ReadData,
                FileShare.None,
                4096,
                FileOptions.None,
                OwnerOnly());
            using var writer = new StreamWriter(stream);
            writer.Write(pem);
        }
        else
        {
            using FileStream stream = new(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });
            using var writer = new StreamWriter(stream);
            writer.Write(pem);
        }
    }

    /// <summary>Full control for the current user, nothing inherited, nobody else.</summary>
    [SupportedOSPlatform("windows")]
    internal static FileSecurity OwnerOnly()
    {
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User
            ?? throw new ToolException("Cannot tell the current Windows user.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
