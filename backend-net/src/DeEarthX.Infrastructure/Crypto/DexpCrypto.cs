using System.Security.Cryptography;
using System.Text;

namespace DeEarthX.Infrastructure.Crypto;

public interface IDexpCrypto
{
    DexpHeader? ParseHeader(byte[] buffer);

    byte[] DeriveKey(string password);

    byte[]? Decrypt(byte[] buffer, string password);

    byte[] Encrypt(byte[] data, string password, byte mode);

    bool IsDexp(byte[] buffer);
}

public sealed record DexpHeader(byte Mode, byte[] Iv, byte[] Data);

public sealed class DexpCrypto : IDexpCrypto
{
    public const string PublicPassword = "DeEarthX-CE";
    public const byte PublicMode = 0;
    public const byte PrivateMode = 1;

    public static readonly byte[] Magic = "DEXP"u8.ToArray();
    public const int HeaderSize = 4 + 1 + 16;

    public DexpHeader? ParseHeader(byte[] buffer)
    {
        if (buffer is null || buffer.Length < HeaderSize)
        {
            return null;
        }

        if (!buffer.AsSpan(0, 4).SequenceEqual(Magic))
        {
            return null;
        }

        var mode = buffer[4];
        var iv = buffer.AsSpan(5, 16).ToArray();
        var data = buffer.AsSpan(HeaderSize).ToArray();
        return new DexpHeader(mode, iv, data);
    }

    public byte[] DeriveKey(string password)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(password));
    }

    public byte[]? Decrypt(byte[] buffer, string password)
    {
        var header = ParseHeader(buffer);
        if (header is null)
        {
            return null;
        }

        try
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = DeriveKey(password);
            aes.IV = header.Iv;

            using var input = new MemoryStream(header.Data);
            using var decryptor = aes.CreateDecryptor();
            using var cs = new CryptoStream(input, decryptor, CryptoStreamMode.Read);
            using var output = new MemoryStream();
            cs.CopyTo(output);
            var plain = output.ToArray();

            if (plain.Length < 4)
            {
                return null;
            }

            var zipMagic = (plain[0] << 8) | plain[1];
            if (zipMagic != 0x504B)
            {
                return null;
            }

            return plain;
        }
        catch
        {
            return null;
        }
    }

    public byte[] Encrypt(byte[] data, string password, byte mode)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = DeriveKey(password);
        aes.GenerateIV();
        var iv = aes.IV;

        byte[] cipher;
        using (var ms = new MemoryStream())
        {
            using (var encryptor = aes.CreateEncryptor())
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(data, 0, data.Length);
                cs.FlushFinalBlock();
            }
            cipher = ms.ToArray();
        }

        var header = new byte[HeaderSize];
        Magic.AsSpan().CopyTo(header.AsSpan(0, 4));
        header[4] = mode;
        Buffer.BlockCopy(iv, 0, header, 5, 16);

        var result = new byte[HeaderSize + cipher.Length];
        Buffer.BlockCopy(header, 0, result, 0, HeaderSize);
        Buffer.BlockCopy(cipher, 0, result, HeaderSize, cipher.Length);
        return result;
    }

    public bool IsDexp(byte[] buffer)
    {
        return buffer is not null && buffer.Length >= 4 && buffer.AsSpan(0, 4).SequenceEqual(Magic);
    }
}
