using System.Text;

namespace RemoteDesktop.Core.Crypto;

/// <summary>
/// RFC 4648 base32 (no padding), lowercase. Used for human-readable device ids and
/// pairing codes — chosen over hex/base64 to avoid ambiguous characters when a person
/// reads a code off one screen and types it on another.
/// </summary>
public static class Base32
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;

        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0)
            sb.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);

        return sb.ToString();
    }
}
