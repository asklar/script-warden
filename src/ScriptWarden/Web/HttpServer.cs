using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ScriptWarden.Web;

/// <summary>An incoming HTTP request (GET/POST) parsed by <see cref="HttpServer"/>.</summary>
internal sealed class HttpRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required IReadOnlyDictionary<string, string> Query { get; init; }
    public string Body { get; init; } = "";
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
    private TcpListener? _listener;

    public HttpServer(int port, Func<HttpRequest, HttpResponse> handler)
    {
        _port = port;
        _handler = handler;
    }

    /// <summary>Binds and begins listening. Once this returns, connections are accepted (queued),
    /// so it's safe to open the browser — no "connection refused" / blank page race.</summary>
    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
    }

    /// <summary>Blocks, serving requests. <see cref="Start"/> must have been called first.</summary>
    public void AcceptLoop()
    {
        TcpListener listener = _listener ?? throw new InvalidOperationException("Start() must be called before AcceptLoop().");
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

        string? requestLine = ReadRequestHeader(stream, out string headers);
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
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            response = HttpResponse.Text("Method Not Allowed", 405);
        }
        else
        {
            string body = ReadBody(stream, headers);
            response = _handler(new HttpRequest { Method = method, Path = path, Query = query, Body = body });
        }

        Write(stream, response);
    }

    private static string ReadBody(NetworkStream stream, string headers)
    {
        int length = ContentLength(headers);
        if (length <= 0 || length > 8 * 1024 * 1024)
        {
            return "";
        }

        byte[] buffer = new byte[length];
        int offset = 0;
        try
        {
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                {
                    break;
                }
                offset += read;
            }
        }
        catch (IOException)
        {
            return "";
        }

        return Encoding.UTF8.GetString(buffer, 0, offset);
    }

    private static int ContentLength(string headers)
    {
        foreach (string line in headers.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line[(colon + 1)..].Trim(), out int len))
                {
                    return len;
                }
            }
        }
        return 0;
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
                into[DecodeComponent(pair)] = "";
            }
            else
            {
                into[DecodeComponent(pair[..eq])] = DecodeComponent(pair[(eq + 1)..]);
            }
        }
    }

    // application/x-www-form-urlencoded: '+' means space (UnescapeDataString alone leaves '+' as-is).
    private static string DecodeComponent(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

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
