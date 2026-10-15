using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tribes.Update;

/// <summary>
/// A public key the client accepts manifest signatures from: an ECDSA P-256 key as
/// SubjectPublicKeyInfo (DER). Its id is the first 8 bytes of the SHA-256 of that DER, in hex.
/// </summary>
public sealed class TrustedKey
{
    private const string P256Oid = "1.2.840.10045.3.1.7";
    private readonly byte[] publicKey;

    /// <summary>Creates a trusted key; throws when the DER is not a P-256 public key.</summary>
    public TrustedKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        publicKey = subjectPublicKeyInfo.ToArray();
        using ECDsa key = Import(publicKey);
        KeyId = ComputeKeyId(publicKey);
    }

    /// <summary>The key id written into signatures.</summary>
    public string KeyId { get; }

    /// <summary>The SubjectPublicKeyInfo DER.</summary>
    public ReadOnlySpan<byte> PublicKey => publicKey;

    /// <summary>The trusted key of a key pair (only the public half is kept).</summary>
    public static TrustedKey FromKey(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new TrustedKey(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>The key id of a SubjectPublicKeyInfo DER.</summary>
    public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
        Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo)[..8]).ToLowerInvariant();

    /// <summary>Whether a signature over the data is valid for this key.</summary>
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        using ECDsa key = Import(publicKey);
        return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static ECDsa Import(byte[] der)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(der, out int read);
            ECParameters parameters = key.ExportParameters(false);
            if (read != der.Length || parameters.Curve.Oid.Value != P256Oid)
            {
                throw new ArgumentException("The key is not an ECDSA P-256 public key.", nameof(der));
            }

            return key;
        }
        catch (CryptographicException e)
        {
            key.Dispose();
            throw new ArgumentException("The key is not an ECDSA P-256 public key.", nameof(der), e);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}

/// <summary>
/// The keys the client trusts. The app carries its own set (<c>UpdateKeys.json</c>, embedded at
/// build time); holding the old and the new key during a rotation lets both sign.
/// </summary>
public sealed class TrustedKeySet
{
    /// <summary>The name of the embedded resource with the app's keys.</summary>
    public const string ResourceName = "Tribes.Update.UpdateKeys.json";

    private static readonly Lazy<TrustedKeySet> Embedded = new(LoadEmbeddedCore);

    /// <summary>Creates a set from keys (duplicates by id are dropped).</summary>
    public TrustedKeySet(IEnumerable<TrustedKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        Keys = keys.GroupBy(k => k.KeyId, StringComparer.Ordinal).Select(g => g.First()).ToArray();
    }

    /// <summary>The keys.</summary>
    public IReadOnlyList<TrustedKey> Keys { get; }

    /// <summary>The keys built into this library.</summary>
    public static TrustedKeySet LoadEmbedded() => Embedded.Value;

    /// <summary>The key with an id, or null.</summary>
    public TrustedKey? Find(string keyId) => Keys.FirstOrDefault(k => k.KeyId == keyId);

    /// <summary>A copy with one more key.</summary>
    public TrustedKeySet With(TrustedKey key) => new(Keys.Append(key));

    /// <summary>A copy without the key with an id.</summary>
    public TrustedKeySet Without(string keyId) => new(Keys.Where(k => k.KeyId != keyId));

    /// <summary>Reads the JSON form: <c>{"keys":[{"id":"…","publicKey":"base64 DER"}]}</c>.</summary>
    public static TrustedKeySet Parse(ReadOnlyMemory<byte> json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var keys = new List<TrustedKey>();
        foreach (JsonElement item in Json.RequiredArray(document.RootElement, "keys"))
        {
            var key = new TrustedKey(Convert.FromBase64String(Json.RequiredString(item, "publicKey")));
            if (key.KeyId != Json.RequiredString(item, "id"))
            {
                throw new FormatException("A trusted key's id does not match its public key.");
            }

            keys.Add(key);
        }

        return new TrustedKeySet(keys);
    }

    /// <summary>The JSON form, as <see cref="Parse"/> reads it.</summary>
    public byte[] Serialize()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, ManifestJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("keys");
            foreach (TrustedKey key in Keys)
            {
                writer.WriteStartObject();
                writer.WriteString("id", key.KeyId);
                writer.WriteString("publicKey", Convert.ToBase64String(key.PublicKey));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    private static TrustedKeySet LoadEmbeddedCore()
    {
        using Stream? stream = typeof(TrustedKeySet).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return new TrustedKeySet([]);
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        try
        {
            return Parse(buffer.ToArray());
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            throw new UpdateException(UpdateErrorKind.Unsupported, "The trusted key list built into this app is damaged.", e);
        }
    }
}

/// <summary>
/// The manifest signature (<c>manifest.json.sig</c>): ECDSA P-256 with SHA-256 over the exact bytes
/// of <c>manifest.json</c>, in IEEE P1363 form (r and s, 32 bytes each, 64 in all), stored as
/// <c>{"algorithm":"ECDSA-P256-SHA256-P1363","keyId":"…","signature":"base64"}</c>.
/// </summary>
public static class ManifestSignature
{
    /// <summary>The algorithm name in the signature file.</summary>
    public const string Algorithm = "ECDSA-P256-SHA256-P1363";

    /// <summary>The largest signature file the client accepts.</summary>
    public const int MaxBytes = 4096;

    private const int SignatureLength = 64;

    /// <summary>Signs manifest bytes with a private key; returns the signature file.</summary>
    public static byte[] Sign(ReadOnlySpan<byte> manifest, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        string keyId = TrustedKey.FromKey(privateKey).KeyId;
        byte[] signature = privateKey.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, ManifestJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("algorithm", Algorithm);
            writer.WriteString("keyId", keyId);
            writer.WriteString("signature", Convert.ToBase64String(signature));
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Checks a signature file against manifest bytes; throws <see cref="UpdateException"/>
    /// (<see cref="UpdateErrorKind.BadSignature"/>) unless a trusted key signed exactly these bytes.
    /// Returns the id of the key that signed.
    /// </summary>
    public static string Verify(ReadOnlySpan<byte> manifest, ReadOnlyMemory<byte> signatureFile, TrustedKeySet keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (signatureFile.Length == 0 || signatureFile.Length > MaxBytes)
        {
            throw Bad("the signature file is missing or too large");
        }

        string keyId;
        byte[] signature;
        try
        {
            using JsonDocument document = JsonDocument.Parse(signatureFile);
            JsonElement root = document.RootElement;
            if (Json.RequiredString(root, "algorithm") != Algorithm)
            {
                throw Bad("the signature algorithm is not " + Algorithm);
            }

            keyId = Json.RequiredString(root, "keyId");
            signature = Convert.FromBase64String(Json.RequiredString(root, "signature"));
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
        {
            throw new UpdateException(UpdateErrorKind.BadSignature, "The signature file is malformed.", e);
        }

        TrustedKey key = keys.Find(keyId) ?? throw Bad($"key {keyId} is not trusted");
        if (signature.Length != SignatureLength || !key.Verify(manifest, signature))
        {
            throw Bad("the signature does not match the manifest");
        }

        return keyId;
    }

    private static UpdateException Bad(string reason) =>
        new(UpdateErrorKind.BadSignature, "Manifest signature rejected: " + reason + ".");
}
