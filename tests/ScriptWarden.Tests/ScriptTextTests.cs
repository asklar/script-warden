using System.Text;
using ScriptWarden.Core;

namespace ScriptWarden.Tests;

public class ScriptTextTests
{
    private const string Sample = "$ErrorActionPreference = \"silentlycontinue\"\r\n### VPN ###";

    [Fact]
    public void Utf16Le_WithBom_DecodesCleanly()
    {
        byte[] bytes = new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(Sample)).ToArray();
        Assert.Equal(Sample, ScriptText.DecodeToText(bytes));
    }

    [Fact]
    public void Utf16Le_WithoutBom_DecodesCleanly()
    {
        byte[] bytes = Encoding.Unicode.GetBytes(Sample); // no preamble
        Assert.Equal(Sample, ScriptText.DecodeToText(bytes));
    }

    [Fact]
    public void Utf16Be_WithBom_DecodesCleanly()
    {
        byte[] bytes = new UnicodeEncoding(bigEndian: true, byteOrderMark: true).GetPreamble()
            .Concat(Encoding.BigEndianUnicode.GetBytes(Sample)).ToArray();
        Assert.Equal(Sample, ScriptText.DecodeToText(bytes));
    }

    [Fact]
    public void Utf8_WithBom_StripsBom()
    {
        byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Sample)).ToArray();
        string decoded = ScriptText.DecodeToText(bytes);
        Assert.Equal(Sample, decoded);
        Assert.DoesNotContain('\uFEFF', decoded);
    }

    [Fact]
    public void Utf8_NoBom_DecodesCleanly()
    {
        Assert.Equal(Sample, ScriptText.DecodeToText(Encoding.UTF8.GetBytes(Sample)));
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ScriptText.DecodeToText([]));
    }

    [Fact]
    public void Utf16_DecodedText_HasNoInterCharacterNulls()
    {
        // Guards against the reported bug: raw UTF-16 bytes shown as UTF-8 render a space (NUL) between
        // every character and a leading replacement char.
        byte[] bytes = Encoding.Unicode.GetBytes("Write-Host hello");
        string decoded = ScriptText.DecodeToText(bytes);
        Assert.Equal("Write-Host hello", decoded);
        Assert.DoesNotContain('\u0000', decoded);
    }
}
