using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides the global fetch() function implementation.
/// </summary>
/// <remarks>
/// Implements the Web API fetch() function for making HTTP requests.
/// Returns a Promise that resolves to a Response object.
///
/// Usage:
///   fetch(url)
///   fetch(url, { method, headers, body, redirect })
/// </remarks>
public static class FetchBuiltIns
{
    // Shared HttpClient instances (best practice for .NET - reuse to benefit from connection pooling).
    // Four clients: {follow, noRedirect} × {with-cookies, without-cookies}.
    // The two with-cookies clients share the process-wide CookieContainer from SharpTSCookieJar.
    private static readonly HttpClient _httpClientFollow = CreateHttpClient(allowAutoRedirect: true, useCookies: false);
    private static readonly HttpClient _httpClientNoRedirect = CreateHttpClient(allowAutoRedirect: false, useCookies: false);
    private static readonly HttpClient _httpClientFollowCookies = CreateHttpClient(allowAutoRedirect: true, useCookies: true);
    private static readonly HttpClient _httpClientNoRedirectCookies = CreateHttpClient(allowAutoRedirect: false, useCookies: true);

    private static HttpClient CreateHttpClient(bool allowAutoRedirect, bool useCookies)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            AllowAutoRedirect = allowAutoRedirect,
            MaxAutomaticRedirections = 20,
            UseCookies = useCookies,
        };
        if (useCookies)
        {
            handler.CookieContainer = SharpTSCookieJar.GlobalContainer;
        }
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "SharpTS/1.0");
        return client;
    }

    /// <summary>
    /// Selects the cached HttpClient for a given (redirectMode, credentials) pair.
    /// </summary>
    /// <remarks>
    /// <c>credentials: 'omit'</c> uses a no-cookie handler so the request is sent
    /// without any stored cookies AND any <c>Set-Cookie</c> response headers are
    /// dropped. <c>'same-origin'</c> and <c>'include'</c> both use the with-cookies
    /// handler — without a top-level realm there's no meaningful distinction, and
    /// <see cref="System.Net.CookieContainer"/>'s domain matching naturally limits
    /// cookies to the host that set them.
    /// </remarks>
    private static HttpClient SelectHttpClient(string redirectMode, string credentials)
    {
        bool follow = redirectMode is not ("manual" or "error");
        bool useCookies = credentials != "omit";
        return (follow, useCookies) switch
        {
            (true, true) => _httpClientFollowCookies,
            (true, false) => _httpClientFollow,
            (false, true) => _httpClientNoRedirectCookies,
            (false, false) => _httpClientNoRedirect,
        };
    }

    /// <summary>
    /// The fetch function as a BuiltInAsyncMethod.
    /// </summary>
    public static BuiltInAsyncMethod FetchMethod { get; } = new("fetch", 1, 2, FetchImpl);

    /// <summary>
    /// Implementation of fetch(url, options?).
    /// </summary>
    private static async Task<object?> FetchImpl(Interpreter interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] == null)
        {
            throw new SharpTSPromiseRejectedException(
                new SharpTSTypeError("fetch requires a URL argument"));
        }

        // Support fetch(Request, options?) - extract url/options from Request object
        string url;
        SharpTSObject? options;

        if (args[0] is SharpTSRequest request)
        {
            url = request.Url;
            // Build options from Request properties
            var requestInit = new SharpTSObject(new Dictionary<string, object?>
            {
                ["method"] = request.Method,
                ["headers"] = request.Headers,
                ["credentials"] = request.Credentials,
            });
            if (request.Body != null)
                requestInit.SetProperty("body", request.Body);

            // Merge with explicit options (options override request properties)
            if (args.Count > 1 && args[1] is SharpTSObject overrideOptions)
            {
                foreach (var kvp in overrideOptions.Fields)
                    requestInit.SetProperty(kvp.Key, kvp.Value);
            }

            options = requestInit;
        }
        else
        {
            url = args[0]?.ToString() ?? throw new SharpTSPromiseRejectedException(
                new SharpTSTypeError("fetch: URL must be a string"));
            options = args.Count > 1 ? args[1] as SharpTSObject : null;
        }

        // Keep the event loop alive while the request is in flight (Node: an
        // active request is a handle). Without this, top-level promise waits
        // see a quiescent loop mid-request and let the process exit early.
        interpreter.Ref();
        try
        {
            var response = await ExecuteFetch(url, options);
            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new SharpTSPromiseRejectedException(
                new SharpTSTypeError($"fetch failed: {ex.Message}"));
        }
        catch (TaskCanceledException ex)
        {
            // Distinguish between abort signal cancellation and HTTP timeout
            if (ex.CancellationToken.IsCancellationRequested && options != null &&
                options.Fields.TryGetValue("signal", out var sig) && sig is SharpTSAbortSignal signal)
            {
                throw new SharpTSPromiseRejectedException(
                    new SharpTSTypeError(signal.Reason?.ToString() ?? "AbortError: The operation was aborted"));
            }
            throw new SharpTSPromiseRejectedException(
                new SharpTSTypeError("fetch: request timeout"));
        }
        catch (UriFormatException)
        {
            throw new SharpTSPromiseRejectedException(
                new SharpTSTypeError($"fetch: Invalid URL '{url}'"));
        }
        finally
        {
            interpreter.Unref();
        }
    }

    /// <summary>
    /// Executes the HTTP request and returns the response.
    /// </summary>
    private static async Task<SharpTSFetchResponse> ExecuteFetch(string url, SharpTSObject? options)
    {
        // Determine HTTP method
        var method = HttpMethod.Get;
        if (options?.Fields.TryGetValue("method", out var methodObj) == true && methodObj is string methodStr)
        {
            method = new HttpMethod(methodStr.ToUpperInvariant());
        }

        using var request = new HttpRequestMessage(method, url);

        // Add headers from options
        if (options?.Fields.TryGetValue("headers", out var headersObj) == true)
        {
            AddHeaders(request, headersObj);
        }

        // Add body for methods that support it
        if (options?.Fields.TryGetValue("body", out var bodyObj) == true && bodyObj != null)
        {
            request.Content = CreateContent(bodyObj, options);
        }

        // Extract AbortSignal for cancellation support
        CancellationToken cancellationToken = default;
        if (options?.Fields.TryGetValue("signal", out var signalObj) == true &&
            signalObj is SharpTSAbortSignal abortSignal)
        {
            if (abortSignal.Aborted)
            {
                throw new SharpTSPromiseRejectedException(
                    new SharpTSTypeError(abortSignal.Reason?.ToString() ?? "AbortError: The operation was aborted"));
            }
            cancellationToken = abortSignal.Token;
        }

        // Handle redirect option: "follow" (default), "error", "manual"
        string redirectMode = "follow";
        if (options?.Fields.TryGetValue("redirect", out var redirectObj) == true && redirectObj is string rm)
        {
            redirectMode = rm;
        }

        // Handle credentials option: "same-origin" (default), "omit", "include"
        // Without a top-level realm, "same-origin" and "include" behave identically
        // and rely on CookieContainer's domain matching.
        string credentials = "same-origin";
        if (options?.Fields.TryGetValue("credentials", out var credsObj) == true && credsObj is string cs)
        {
            credentials = cs;
        }

        var client = SelectHttpClient(redirectMode, credentials);
        var response = await client.SendAsync(request, cancellationToken);

        // For "error" mode, throw on redirect responses
        if (redirectMode == "error" && (int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
        {
            response.Dispose();
            throw new HttpRequestException("fetch failed: redirect mode is set to 'error'");
        }

        // Get the final URL (after any redirects)
        var finalUrl = request.RequestUri?.ToString() ?? url;
        bool redirected = false;
        if (response.RequestMessage?.RequestUri != null)
        {
            finalUrl = response.RequestMessage.RequestUri.ToString();
            // Detect if a redirect occurred by comparing request URIs
            redirected = !string.Equals(
                request.RequestUri?.ToString(), response.RequestMessage.RequestUri.ToString(),
                StringComparison.Ordinal);
        }

        return new SharpTSFetchResponse(response, finalUrl, redirected);
    }

    /// <summary>
    /// Adds headers from the options object to the request.
    /// Supports both SharpTSObject and SharpTSHeaders as input.
    /// </summary>
    private static void AddHeaders(HttpRequestMessage request, object? headersObj)
    {
        IEnumerable<KeyValuePair<string, string>>? headerEntries = null;

        if (headersObj is SharpTSHeaders headersInstance)
        {
            headerEntries = headersInstance.GetEntries();
        }
        else if (headersObj is SharpTSObject headersDict)
        {
            headerEntries = headersDict.Fields.Select(kv =>
                new KeyValuePair<string, string>(kv.Key, kv.Value?.ToString() ?? ""));
        }

        if (headerEntries == null) return;

        foreach (var kv in headerEntries)
        {
            var headerName = kv.Key;
            var headerValue = kv.Value;

            // Content-Type and other content headers go on the content, not the request
            if (headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                // Content-Type will be set when we create the content
                continue;
            }

            try
            {
                request.Headers.TryAddWithoutValidation(headerName, headerValue);
            }
            catch
            {
                // Ignore invalid headers
            }
        }
    }

    /// <summary>
    /// Creates HTTP content from the body object.
    /// </summary>
    private static HttpContent CreateContent(object? bodyObj, SharpTSObject? options)
    {
        // Determine content type from headers
        string? contentType = null;
        if (options?.Fields.TryGetValue("headers", out var headersObj) == true)
        {
            if (headersObj is SharpTSHeaders headersInstance)
            {
                contentType = headersInstance.Get("Content-Type");
            }
            else if (headersObj is SharpTSObject headersDict)
            {
                foreach (var kv in headersDict.Fields)
                {
                    if (kv.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        contentType = kv.Value?.ToString();
                        break;
                    }
                }
            }
        }

        HttpContent content;

        switch (bodyObj)
        {
            case string str:
                content = new StringContent(str, Encoding.UTF8);
                break;

            case SharpTSBuffer buffer:
                content = new ByteArrayContent(buffer.Data);
                break;

            case SharpTSObject obj:
                // Assume JSON if it's an object
                var json = SerializeToJson(obj);
                content = new StringContent(json, Encoding.UTF8, "application/json");
                break;

            case SharpTSArray arr:
                // Assume JSON for arrays too
                var jsonArr = SerializeToJson(arr);
                content = new StringContent(jsonArr, Encoding.UTF8, "application/json");
                break;

            default:
                content = new StringContent(bodyObj?.ToString() ?? "", Encoding.UTF8);
                break;
        }

        // Set content type if explicitly provided
        if (contentType != null)
        {
            if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            {
                content.Headers.ContentType = mediaType;
            }
        }

        return content;
    }

    /// <summary>
    /// Gets a member from the fetch namespace (there aren't any static properties).
    /// </summary>
    public static object? GetMember(string name)
    {
        // fetch itself is a function, not an object with members
        return null;
    }

    /// <summary>
    /// Serializes a SharpTS value to JSON string.
    /// </summary>
    private static string SerializeToJson(object? value)
    {
        return JsonSerializer.Serialize(ConvertToSerializable(value));
    }

    /// <summary>
    /// Converts SharpTS runtime values to .NET types for JSON serialization.
    /// </summary>
    private static object? ConvertToSerializable(object? value)
    {
        return value switch
        {
            null => null,
            bool b => b,
            double d => d,
            string s => s,
            SharpTSArray arr => arr.Select(ConvertToSerializable).ToArray(),
            SharpTSObject obj => obj.Fields.ToDictionary(kv => kv.Key, kv => ConvertToSerializable(kv.Value)),
            SharpTSBuffer buf => Convert.ToBase64String(buf.Data),
            SharpTSUndefined => null,
            _ => value.ToString()
        };
    }
}
