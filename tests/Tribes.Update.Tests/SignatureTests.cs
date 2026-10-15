using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class SignatureTests
{
    private static readonly byte[] Manifest = ManifestJson.Serialize(TestData.Manifest("1.0.0", 1, [TestData.Asset("content-1.0.0-r1.pck", [1, 2, 3])]));

    [Fact]
    public void Verify_AcceptsTheSignersManifest()
    {
        using ECDsa key = TestData.NewKey();
        byte[] signature = ManifestSignature.Sign(Manifest, key);

        string keyId = ManifestSignature.Verify(Manifest, signature, TestData.Trust(key));

        Assert.Equal(TrustedKey.FromKey(key).KeyId, keyId);
        Assert.Equal(16, keyId.Length);
    }

    [Fact]
    public void Verify_RejectsTamperedManifest()
    {
        using ECDsa key = TestData.NewKey();
        byte[] signature = ManifestSignature.Sign(Manifest, key);
        byte[] tampered = (byte[])Manifest.Clone();
        tampered[tampered.Length / 2] ^= 1;

        AssertBad(() => ManifestSignature.Verify(tampered, signature, TestData.Trust(key)));
    }

    [Fact]
    public void Verify_RejectsUnknownKey()
    {
        using ECDsa signer = TestData.NewKey();
        using ECDsa trusted = TestData.NewKey();

        AssertBad(() => ManifestSignature.Verify(Manifest, ManifestSignature.Sign(Manifest, signer), TestData.Trust(trusted)));
    }

    [Fact]
    public void Verify_RejectsWrongKeyUnderTrustedId()
    {
        using ECDsa trusted = TestData.NewKey();
        using ECDsa attacker = TestData.NewKey();
        byte[] forged = WithField(ManifestSignature.Sign(Manifest, attacker), "keyId", TrustedKey.FromKey(trusted).KeyId);

        AssertBad(() => ManifestSignature.Verify(Manifest, forged, TestData.Trust(trusted)));
    }

    [Theory]
    [InlineData("algorithm", "ECDSA-P384-SHA384")]
    [InlineData("signature", "AAAA")]
    [InlineData("signature", "not base64!")]
    public void Verify_RejectsMalformedSignatureFields(string field, string value)
    {
        using ECDsa key = TestData.NewKey();
        byte[] signature = WithField(ManifestSignature.Sign(Manifest, key), field, value);

        AssertBad(() => ManifestSignature.Verify(Manifest, signature, TestData.Trust(key)));
    }

    [Fact]
    public void Verify_RejectsMissingOrOversizedOrGarbageSignature()
    {
        using ECDsa key = TestData.NewKey();
        TrustedKeySet keys = TestData.Trust(key);

        AssertBad(() => ManifestSignature.Verify(Manifest, Array.Empty<byte>(), keys));
        AssertBad(() => ManifestSignature.Verify(Manifest, new byte[ManifestSignature.MaxBytes + 1], keys));
        AssertBad(() => ManifestSignature.Verify(Manifest, Encoding.UTF8.GetBytes("{\"keyId\":1}"), keys));
        AssertBad(() => ManifestSignature.Verify(Manifest, Encoding.UTF8.GetBytes("garbage"), keys));
    }

    [Fact]
    public void Verify_RejectsInvalidUtf8()
    {
        using ECDsa key = TestData.NewKey();
        byte[] signature = [.. Encoding.UTF8.GetBytes("{\"algorithm\":\""), 0xFF, 0xFE, .. Encoding.UTF8.GetBytes("\",\"keyId\":\"x\",\"signature\":\"AA==\"}")];

        AssertBad(() => ManifestSignature.Verify(Manifest, signature, TestData.Trust(key)));
    }

    [Fact]
    public void Verify_FailsClosedWithoutKeys()
    {
        using ECDsa key = TestData.NewKey();

        AssertBad(() => ManifestSignature.Verify(Manifest, ManifestSignature.Sign(Manifest, key), new TrustedKeySet([])));
    }

    [Fact]
    public void TrustedKeySet_RoundTripsAndEdits()
    {
        using ECDsa a = TestData.NewKey();
        using ECDsa b = TestData.NewKey();
        TrustedKeySet set = TestData.Trust(a).With(TrustedKey.FromKey(b)).With(TrustedKey.FromKey(a));

        TrustedKeySet parsed = TrustedKeySet.Parse(set.Serialize());

        Assert.Equal(2, parsed.Keys.Count);
        Assert.NotNull(parsed.Find(TrustedKey.FromKey(b).KeyId));
        Assert.Single(parsed.Without(TrustedKey.FromKey(a).KeyId).Keys);
        Assert.True(TrustedKey.FromKey(a).PublicKey.SequenceEqual(parsed.Find(TrustedKey.FromKey(a).KeyId)!.PublicKey));
    }

    [Fact]
    public void TrustedKeySet_RejectsMismatchedId()
    {
        using ECDsa key = TestData.NewKey();
        string json = $$"""{"keys":[{"id":"0000000000000000","publicKey":"{{Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}}"}]}""";

        Assert.Throws<FormatException>(() => TrustedKeySet.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void TrustedKey_RejectsOtherKeyTypes()
    {
        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using RSA rsa = RSA.Create(2048);

        Assert.Throws<ArgumentException>(() => new TrustedKey(p384.ExportSubjectPublicKeyInfo()));
        Assert.Throws<ArgumentException>(() => new TrustedKey(rsa.ExportSubjectPublicKeyInfo()));
        Assert.Throws<ArgumentException>(() => new TrustedKey([1, 2, 3]));
    }

    [Fact]
    public void EmbeddedKeys_Load()
    {
        TrustedKeySet embedded = TrustedKeySet.LoadEmbedded();

        Assert.Same(embedded, TrustedKeySet.LoadEmbedded());
        Assert.All(embedded.Keys, k => Assert.Equal(TrustedKey.ComputeKeyId(k.PublicKey), k.KeyId));
    }

    private static void AssertBad(Action verify)
    {
        UpdateException error = Assert.Throws<UpdateException>(verify);
        Assert.Equal(UpdateErrorKind.BadSignature, error.Kind);
    }

    private static byte[] WithField(byte[] signatureFile, string field, string value)
    {
        using JsonDocument document = JsonDocument.Parse(signatureFile);
        var fields = new System.Collections.Generic.Dictionary<string, string>();
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            fields[property.Name] = property.Value.GetString()!;
        }

        fields[field] = value;
        return JsonSerializer.SerializeToUtf8Bytes(fields);
    }
}
