using System.Text;

namespace ScriptWarden.Core;

/// <summary>
/// Decodes captured script bytes to text for <em>display</em>. Captured content is stored verbatim
/// (so downloads are byte-exact), but scripts pushed by management tooling are frequently UTF-16 —
/// often with a BOM, sometimes without — so rendering the raw bytes as UTF-8 produces mojibake
/// (a leading <c>◇◇</c> and a space between every character). This honors a BOM when present and
/// otherwise sniffs UTF-16 from the byte distribution, falling back to UTF-8.
/// </summary>
public static class ScriptText
{
    public static string DecodeToText(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return string.Empty;
        }

        // Byte order marks take precedence (UTF-32 checked before UTF-16 to disambiguate FF FE …).
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return new UTF32Encoding(bigEndian: false, byteOrderMark: false).GetString(bytes, 4, bytes.Length - 4);
        }
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetString(bytes, 4, bytes.Length - 4);
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 LE
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 BE
        }

        // No BOM: sniff UTF-16 by where the NUL bytes fall. ASCII-range text encoded as UTF-16 LE
        // has NUL high bytes at odd offsets (and BE at even offsets); UTF-8/ASCII text has ~none.
        int sample = Math.Min(bytes.Length & ~1, 8192);
        int nulOdd = 0, nulEven = 0, pairs = 0;
        for (int i = 0; i + 1 < sample; i += 2)
        {
            if (bytes[i] == 0) nulEven++;
            if (bytes[i + 1] == 0) nulOdd++;
            pairs++;
        }
        if (pairs > 0)
        {
            double threshold = pairs * 0.30;
            if (nulOdd > threshold && nulOdd > nulEven)
            {
                return Encoding.Unicode.GetString(bytes);
            }
            if (nulEven > threshold && nulEven > nulOdd)
            {
                return Encoding.BigEndianUnicode.GetString(bytes);
            }
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes);
    }
}
