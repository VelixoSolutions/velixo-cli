using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Velixo.Cli.Auth;

/// <summary>
/// Receives the authorization redirect on <c>http://127.0.0.1:&lt;random port&gt;/callback</c>, the
/// RFC 8252 loopback pattern for a native app.
/// </summary>
public sealed class LoopbackListener : IDisposable
{
    private const string CallbackPath = "/callback";

    private const string DonePage =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Velixo CLI</title></head>"
        + "<body style=\"font-family:sans-serif;margin:3em\"><h1>Signed in</h1>"
        + "<p>You can close this window and return to the terminal.</p></body></html>";

    private readonly HttpListener _listener;

    private LoopbackListener(HttpListener listener, int port)
    {
        _listener = listener;
        RedirectUri = new Uri($"http://127.0.0.1:{port}{CallbackPath}");
    }

    public Uri RedirectUri { get; }

    public static LoopbackListener Start()
    {
        var port = FreeLoopbackPort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        return new LoopbackListener(listener, port);
    }

    /// <summary>A port nothing listens on now. Another process could take it before it is used, which the caller accepts.</summary>
    public static int FreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    /// <summary>Waits for the browser to arrive on the callback path and returns the full URL it requested.</summary>
    public async Task<Uri> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration stop = cancellationToken.Register(_listener.Stop);

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is HttpListenerException or ObjectDisposedException)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            Uri? requested = context.Request.Url;
            var isCallback = requested is not null
                && string.Equals(requested.AbsolutePath.TrimEnd('/'), CallbackPath, StringComparison.Ordinal);

            Respond(context.Response, isCallback ? HttpStatusCode.OK : HttpStatusCode.NotFound, isCallback ? DonePage : "Not found");

            if (isCallback)
            {
                return new Uri(RedirectUri, requested!.PathAndQuery);
            }
        }
    }

    public void Dispose() => ((IDisposable)_listener).Dispose();

    private static void Respond(HttpListenerResponse response, HttpStatusCode status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = (int)status;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
        response.Close();
    }
}
