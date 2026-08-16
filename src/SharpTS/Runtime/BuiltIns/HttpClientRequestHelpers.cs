using System.Net;
using System.Net.Http;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Shared BCL plumbing for the Node-style http(s) client (<c>SharpTSClientRequest</c>).
/// Distinct from the <c>fetch</c> client because Node's <c>http.request</c> does NOT
/// follow redirects or auto-decompress — it hands the raw response to the caller.
/// </summary>
public static class HttpClientRequestHelpers
{
    private static HttpClient? _plainClient;
    private static HttpClient? _insecureTlsClient;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets a shared HttpClient for client requests. No auto-redirect and no auto-decompression
    /// (Node semantics). When <paramref name="rejectUnauthorized"/> is false a separate client
    /// that accepts any server certificate is used (for https with self-signed certs).
    /// </summary>
    public static HttpClient GetClient(bool rejectUnauthorized = true)
    {
        if (rejectUnauthorized)
        {
            if (_plainClient != null) return _plainClient;
            lock (_lock)
            {
                _plainClient ??= Build(rejectUnauthorized: true);
                return _plainClient;
            }
        }
        else
        {
            if (_insecureTlsClient != null) return _insecureTlsClient;
            lock (_lock)
            {
                _insecureTlsClient ??= Build(rejectUnauthorized: false);
                return _insecureTlsClient;
            }
        }
    }

    private static HttpClient Build(bool rejectUnauthorized)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false
        };
        if (!rejectUnauthorized)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        // Finite safety-net timeout so a stuck request can't pin the event loop forever
        // (the request still manages its own CancellationToken for abort()/setTimeout()).
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };
    }
}
