using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Tms.SharedKernel.Security;

namespace Tms.UnitTests;

public class FieldEncryptionTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmFieldEncryptor Create(string currentId, params (string Id, string Key)[] keys) =>
        new(Options.Create(new FieldEncryptionOptions { CurrentKeyId = currentId, Keys = keys.ToDictionary(k => k.Id, k => k.Key) }));

    [Fact]
    public void RoundTrips_AndNeverStoresThePlaintext()
    {
        var encryptor = Create("1", ("1", NewKey()));

        var stored = encryptor.Encrypt("50100123456789");

        stored.ShouldStartWith("v1.1.");
        stored.ShouldNotContain("50100123456789");
        encryptor.Decrypt(stored).ShouldBe("50100123456789");
    }

    [Fact]
    public void UsesAFreshNonce_SoEqualInputsLookDifferent()
    {
        var encryptor = Create("1", ("1", NewKey()));

        encryptor.Encrypt("same").ShouldNotBe(encryptor.Encrypt("same"));
    }

    [Fact]
    public void DetectsTampering()
    {
        var encryptor = Create("1", ("1", NewKey()));
        var stored = encryptor.Encrypt("secret-value");
        var bytes = Convert.FromBase64String(stored[(stored.LastIndexOf('.') + 1)..]);
        bytes[^1] ^= 0xFF;
        var tampered = stored[..(stored.LastIndexOf('.') + 1)] + Convert.ToBase64String(bytes);

        Should.Throw<CryptographicException>(() => encryptor.Decrypt(tampered));
    }

    [Fact]
    public void Rotation_NewDataUsesTheNewKey_OldDataStillReads()
    {
        var oldKey = NewKey();
        var before = Create("1", ("1", oldKey)).Encrypt("legacy-account");

        var rotated = Create("2", ("1", oldKey), ("2", NewKey()));

        rotated.Encrypt("fresh-account").ShouldStartWith("v1.2.");
        rotated.Decrypt(before).ShouldBe("legacy-account");
    }

    [Fact]
    public void ADifferentKey_CannotReadTheData()
    {
        var stored = Create("1", ("1", NewKey())).Encrypt("secret");

        Should.Throw<CryptographicException>(() => Create("1", ("1", NewKey())).Decrypt(stored));
    }

    [Fact]
    public void LegacyPlaintextPassesThrough_SoAnExistingColumnCanBeEncryptedInPlace()
    {
        Create("1", ("1", NewKey())).Decrypt("123456789012").ShouldBe("123456789012");
    }

    [Fact]
    public void RefusesToStartWithoutAValidCurrentKey()
    {
        Should.Throw<InvalidOperationException>(() => Create("1"));
        Should.Throw<InvalidOperationException>(() => Create("1", ("1", Convert.ToBase64String(new byte[16]))));
    }
}
