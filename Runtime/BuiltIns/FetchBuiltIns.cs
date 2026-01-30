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
    // Shared HttpClient instance (best practice for .NET)
    // Use a handler with automatic decompression and cookie handling
    private static readonly HttpClient _httpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 20
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "SharpTS/1.0");
        return client;
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

        var url = args[0]?.ToString() ?? throw new SharpTSPromiseRejectedException(
            new SharpTSTypeError("fetch: URL must be a string"));

        // Parse options if provided
        var options = args.Count > 1 ? args[1] as SharpTSObject : null;

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
        catch (TaskCanceledException)
        {
            throw new SharpTSPromiseRejectedException(
                new SharpTSTypeError("fetch: request timeout"));
        }
        catch (UriFormatException)
        {
            throw new SharpTSPromiseRejectedException(
                new SharpTSTypeError($"fetch: Invalid URL '{url}'"));
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

        // Handle redirect option (follow, error, manual)
        // Note: We use the default HttpClient redirect behavior (follow)
        // For "manual" or "error", we'd need a different HttpClient configuration
        // This is a simplified implementation

        var response = await _httpClient.SendAsync(request);

        // Get the final URL (after any redirects)
        var finalUrl = request.RequestUri?.ToString() ?? url;
        if (response.RequestMessage?.RequestUri != null)
        {
            finalUrl = response.RequestMessage.RequestUri.ToString();
        }

        return new SharpTSFetchResponse(response, finalUrl);
    }

    /// <summary>
    /// Adds headers from the options object to the request.
    /// </summary>
    private static void AddHeaders(HttpRequestMessage request, object? headersObj)
    {
        if (headersObj is SharpTSObject headersDict)
        {
            foreach (var kv in headersDict.Fields)
            {
                var headerName = kv.Key;
                var headerValue = kv.Value?.ToString() ?? "";

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
    }

    /// <summary>
    /// Creates HTTP content from the body object.
    /// </summary>
    private static HttpContent CreateContent(object? bodyObj, SharpTSObject? options)
    {
        // Determine content type from headers
        string? contentType = null;
        if (options?.Fields.TryGetValue("headers", out var headersObj) == true &&
            headersObj is SharpTSObject headersDict)
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
            SharpTSArray arr => arr.Elements.Select(ConvertToSerializable).ToArray(),
            SharpTSObject obj => obj.Fields.ToDictionary(kv => kv.Key, kv => ConvertToSerializable(kv.Value)),
            SharpTSBuffer buf => Convert.ToBase64String(buf.Data),
            SharpTSUndefined => null,
            _ => value.ToString()
        };
    }
}
