using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MOROVelocityX.Services;

public sealed class EncryptionService
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Pbkdf2Iterations = 100_000;
    private static readonly byte[] StorageSalt = Encoding.UTF8.GetBytes("MOROVelocityX-Storage-Salt-v1");
    private static readonly byte[] AppSecret = Encoding.UTF8.GetBytes("MOROVelocityX-Encryption-Key-v1");

    public byte[] Encrypt<T>(T data)
    {
        var json = JsonSerializer.Serialize(data);
        return EncryptString(json);
    }

    public T? Decrypt<T>(byte[] encryptedData)
    {
        var json = DecryptString(encryptedData);
        if (string.IsNullOrEmpty(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json);
    }

    public byte[] EncryptString(string plainText)
    {
        var key = DeriveKey(AppSecret, StorageSalt);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var result = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, result, NonceSize + TagSize, cipherBytes.Length);
        return result;
    }

    public string DecryptString(byte[] encryptedData)
    {
        if (encryptedData.Length <= NonceSize + TagSize)
        {
            return string.Empty;
        }

        var key = DeriveKey(AppSecret, StorageSalt);
        var nonce = encryptedData.AsSpan(0, NonceSize);
        var tag = encryptedData.AsSpan(NonceSize, TagSize);
        var cipherBytes = encryptedData.AsSpan(NonceSize + TagSize);
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public void SaveEncrypted<T>(string filePath, T data)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encrypted = Encrypt(data);
        File.WriteAllBytes(filePath, encrypted);
    }

    public T? LoadEncrypted<T>(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return default;
        }

        try
        {
            var encrypted = File.ReadAllBytes(filePath);
            return Decrypt<T>(encrypted);
        }
        catch
        {
            return default;
        }
    }

    private static byte[] DeriveKey(byte[] secret, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(secret, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
