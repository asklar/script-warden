using ScriptWarden.Web;

namespace ScriptWarden.Cli.Tests;

public class HttpServerLifecycleTests
{
    [Fact]
    public async Task Stop_UnblocksAcceptLoopPromptly()
    {
        // Port 0 lets the OS pick a free loopback port, so the test never collides with a real serve.
        var server = new HttpServer(0, _ => HttpResponse.Text("ok"));
        server.Start();

        var loop = Task.Run(server.AcceptLoop);

        // Simulate Ctrl+C: closing the listener must break the blocking AcceptTcpClient so the loop returns.
        server.Stop();

        Task completed = await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(completed == loop, "AcceptLoop did not return after Stop().");
        await loop; // surface any fault
    }

    [Fact]
    public async Task Stop_BeforeAccept_IsSafe()
    {
        var server = new HttpServer(0, _ => HttpResponse.Text("ok"));
        server.Start();
        server.Stop();

        // AcceptLoop should observe _stopping and return immediately without throwing.
        var loop = Task.Run(server.AcceptLoop);
        Task completed = await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(completed == loop, "AcceptLoop did not return promptly.");
        await loop;
    }
}
