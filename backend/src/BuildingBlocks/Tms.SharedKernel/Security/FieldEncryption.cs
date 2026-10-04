using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Tms.SharedKernel.Security;

public sealed class FieldEncryptionOptions
{
    public const string SectionName = "FieldEncryption";

    /// <summary>Id of the key used for new encryptions. Older ids stay in <see cref="Keys"/> so existing data still decrypts.</summary>
    public string CurrentKeyId { get; init; } = "1";

    /// <summary>Key id → base64 of a 32-byte key. Supply from a secret store, never from a committed file.</summary>
    public Dictionary<string, string> Keys { get; init; } = [];
}

/// <summary>Application-level encryption for sensitive columns (bank account numbers).</summary>
public interface IFieldEncryptor
{
    string Encrypt(string plaintext);

    string Decrypt(string stored);
}

/// <summary>
/// AES-256-GCM with a fresh random nonce per value, authenticated (tampering is detected) and tagged with a key id so
/// keys can be rotated: stored form is <c>v1.{keyId}.{base64(nonce|tag|ciphertext)}</c>.
/// Values without the <c>v1.</c> prefix are treated as legacy plaintext and returned unchanged, so a column can be
/// encrypted in place and old rows are upgraded the next time they are written.
/// </summary>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    private const string Prefix = "v1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _currentId;
    private readonly Dictionary<string, byte[]> _keys;

    public AesGcmFieldEncryptor(IOptions<FieldEncryptionOptions> options)
    {
        var o = options.Value;
        _currentId = o.CurrentKeyId;
        _keys = o.Keys.ToDictionary(kv => kv.Key, kv => Convert.FromBase64String(kv.Value), StringComparer.Ordinal);

        if (!_keys.TryGetValue(_currentId, out var current) || current.Length != 32)
        {
            throw new InvalidOperationException(
                $"FieldEncryption:Keys:{_currentId} must be a base64-encoded 32-byte key (generate one with: openssl rand -base64 32).");
        }

        if (_keys.Values.Any(k => k.Length != 32))
        {
            throw new InvalidOperationException("Every FieldEncryption key must be 32 bytes.");
        }
    }

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_keys[_currentId], TagSize))
        {
            aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(_currentId));
        }

        return $"{Prefix}{_currentId}.{Convert.ToBase64String([.. nonce, .. tag, .. cipher])}";
    }

    public string Decrypt(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return stored; // legacy plaintext
        }

        var rest = stored[Prefix.Length..];
        var dot = rest.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0 || !_keys.TryGetValue(rest[..dot], out var key))
        {
            throw new CryptographicException("The value was encrypted with a key that is not configured.");
        }

        var keyId = rest[..dot];
        var payload = Convert.FromBase64String(rest[(dot + 1)..]);
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(keyId));
        return Encoding.UTF8.GetString(plain);
    }
}
