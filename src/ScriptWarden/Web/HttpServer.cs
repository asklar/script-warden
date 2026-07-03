using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScriptWarden.Web;

/// <summary>An incoming HTTP request (GET only) parsed by <see cref="HttpServer"/>.</summary>
internal sealed class HttpRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required IReadOnlyDictionary<string, string> Query { get; init; }
}

/// <summary>A response to write back.</summary>
internal sealed class HttpResponse
{
    public int Status { get; set; } = 200;
    public string ContentType { get; set; } = "text/plain; charset=utf-8";
    public byte[] Body { get; set; } = [];
    public string? ContentDisposition { get; set; }

    public static HttpResponse Text(string body, int status = 200) => new()
    {
        Status = status,
        ContentType = "text/plain; charset=utf-8",
        Body = Encoding.UTF8.GetBytes(body),
    };

    public static HttpResponse Json(string json, int status = 200) => new()
    {
        Status = status,
        ContentType = "application/json; charset=utf-8",
        Body = Encoding.UTF8.GetBytes(json),
    };

    public static HttpResponse Html(string html) => new()
    {
        ContentType = "text/html; charset=utf-8",
        Body = Encoding.UTF8.GetBytes(html),
    };
}

/// <summary>
/// A tiny loopback-only HTTP/1.1 server built on <see cref="TcpListener"/>. Using a raw socket
/// (instead of HttpListener/http.sys) avoids URL-ACL reservations, so a non-admin user can view
/// their own audit trail. Handles GET requests only.
/// </summary>
internal sealed class HttpServer
{
    private readonly int _port;
    private readonly Func<HttpRequest, HttpResponse> _handler;

    public HttpServer(int port, Func<HttpRequest, HttpResponse> handler)
    {
        _port = port;
        _handler = handler;
    }

    public void Run()
    {
        var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();
        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();
            // Handle sequentially; the viewer is single-user and low volume. Isolate failures.
            try
            {
                HandleClient(client);
            }
            catch
            {
                // ignore malformed clients
            }
            finally
            {
                client.Dispose();
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        using NetworkStream stream = client.GetStream();
        stream.ReadTimeout = 5000;

        string? requestLine = ReadRequestHeader(stream, out _);
        if (requestLine is null)
        {
            return;
        }

        string[] parts = requestLine.Split(' ');
        if (parts.Length < 2)
        {
            Write(stream, HttpResponse.Text("Bad Request", 400));
            return;
        }

        string method = parts[0];
        string rawTarget = parts[1];
        string path = rawTarget;
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int q = rawTarget.IndexOf('?');
        if (q >= 0)
        {
            path = rawTarget[..q];
            ParseQuery(rawTarget[(q + 1)..], query);
        }

        path = Uri.UnescapeDataString(path);

        HttpResponse response;
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            response = HttpResponse.Text("Method Not Allowed", 405);
        }
        else
        {
            response = _handler(new HttpRequest { Method = method, Path = path, Query = query });
        }

        Write(stream, response);
    }

    private static string? ReadRequestHeader(NetworkStream stream, out string headers)
    {
        headers = "";
        var sb = new StringBuilder();
        int b;
        int consecutive = 0;
        try
        {
            while ((b = stream.ReadByte()) != -1)
            {
                sb.Append((char)b);
                if (b == '\n')
                {
                    consecutive++;
                    if (consecutive == 2)
                    {
                        break;
                    }
                }
                else if (b != '\r')
                {
                    consecutive = 0;
                }
                if (sb.Length > 64 * 1024)
                {
                    break;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }

        string text = sb.ToString();
        int firstLineEnd = text.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return null;
        }

        headers = text;
        return text[..firstLineEnd].TrimEnd('\r', '\n');
    }

    private static void ParseQuery(string query, Dictionary<string, string> into)
    {
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq < 0)
            {
                into[Uri.UnescapeDataString(pair)] = "";
            }
            else
            {
                into[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }
    }

    private static void Write(NetworkStream stream, HttpResponse response)
    {
        var header = new StringBuilder();
        header.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(ReasonPhrase(response.Status)).Append("\r\n");
        header.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        header.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
        if (response.ContentDisposition is not null)
        {
            header.Append("Content-Disposition: ").Append(response.ContentDisposition).Append("\r\n");
        }
        header.Append("Cache-Control: no-store\r\n");
        header.Append("X-Content-Type-Options: nosniff\r\n");
        header.Append("Connection: close\r\n\r\n");

        byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
        stream.Write(headerBytes, 0, headerBytes.Length);
        if (response.Body.Length > 0)
        {
            stream.Write(response.Body, 0, response.Body.Length);
        }
        stream.Flush();
    }

    private static string ReasonPhrase(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _ => "OK",
    };
}
