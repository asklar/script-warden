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
    public async Task Stop_AbortsHeldIdleConnection_WithoutWaitingForReadTimeout()
    {
        // Regression: a browser keep-alive/preconnect that sits idle used to block the single-threaded
        // loop in a 5s read, so Ctrl+C appeared to "not work". Stop() must close the in-flight socket
        // so the blocked read aborts immediately.
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var server = new HttpServer(0, _ => HttpResponse.Text("ok"), log.Enqueue);
        server.Start();
        var loop = Task.Run(server.AcceptLoop);

        // Open a connection and send nothing, so the server accepts it and blocks reading the request.
        using var idle = new System.Net.Sockets.TcpClient();
        await idle.ConnectAsync(System.Net.IPAddress.Loopback, server.BoundPort);
        await Task.Delay(300); // let the server get parked in the blocking read

        var sw = System.Diagnostics.Stopwatch.StartNew();
        server.Stop();

        Task completed = await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.True(completed == loop, "AcceptLoop did not return after Stop() while a connection was held.");
        await loop;
        // Must be well under the 5s read timeout — the socket close, not the timeout, ended the read.
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Shutdown took {sw.ElapsedMilliseconds}ms; expected the held read to be aborted immediately.");
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
