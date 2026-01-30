using System.Net;
using System.Net.Http;
using System.Text;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Helper methods for compiled fetch support.
/// These methods are called from emitted IL to handle complex operations.
/// </summary>
public static class FetchHelpers
{
    // Shared HttpClient for compiled mode (matches interpreter behavior)
    private static HttpClient? _sharedHttpClient;
    private static readonly object _httpClientLock = new();

    /// <summary>
    /// Gets or creates the shared HttpClient instance.
    /// </summary>
    public static HttpClient GetHttpClient()
    {
        if (_sharedHttpClient != null) return _sharedHttpClient;

        lock (_httpClientLock)
        {
            if (_sharedHttpClient != null) return _sharedHttpClient;

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 20
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "SharpTS/1.0");
            _sharedHttpClient = client;
            return client;
        }
    }

    /// <summary>
    /// Performs a fetch request and returns an array with the result data.
    /// Returns object[] { success (bool), status (double), statusText (string), ok (bool), url (string), headers (Dictionary), bodyBytes (byte[]), errorMessage (string?) }
    /// This is the main entry point for compiled fetch.
    /// </summary>
    public static object?[] PerformFetch(object? urlObj, object? optionsObj)
    {
        try
        {
            var url = urlObj?.ToString() ?? "";
            var client = GetHttpClient();

            // Parse method from options
            var method = HttpMethod.Get;
            Dictionary<string, object?>? headers = null;
            object? body = null;

            if (optionsObj is Dictionary<string, object?> options)
            {
                if (options.TryGetValue("method", out var methodVal) && methodVal != null)
                {
                    method = new HttpMethod(methodVal.ToString()!.ToUpperInvariant());
                }
                if (options.TryGetValue("headers", out var headersVal))
                {
                    headers = headersVal as Dictionary<string, object?>;
                }
                if (options.TryGetValue("body", out var bodyVal))
                {
                    body = bodyVal;
                }
            }
            else if (optionsObj is SharpTSObject tsOptions)
            {
                if (tsOptions.Fields.TryGetValue("method", out var methodVal) && methodVal != null)
                {
                    method = new HttpMethod(methodVal.ToString()!.ToUpperInvariant());
                }
                if (tsOptions.Fields.TryGetValue("headers", out var headersVal) && headersVal is SharpTSObject headersObj)
                {
                    headers = headersObj.Fields.ToDictionary(kv => kv.Key, kv => kv.Value);
                }
                if (tsOptions.Fields.TryGetValue("body", out var bodyVal))
                {
                    body = bodyVal;
                }
            }

            using var request = new HttpRequestMessage(method, url);

            // Apply headers
            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    if (kv.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        continue;
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value?.ToString() ?? "");
                }
            }

            // Apply body
            if (body != null && body is not SharpTSUndefined)
            {
                var bodyStr = body is string s ? s : body.ToString() ?? "";
                request.Content = new StringContent(bodyStr);
            }

            // Execute request synchronously (blocking)
            var response = client.SendAsync(request).GetAwaiter().GetResult();
            var bodyBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var status = (double)response.StatusCode;
            var ok = response.IsSuccessStatusCode;
            var statusText = response.ReasonPhrase ?? "";
            var responseHeaders = ExtractResponseHeaders(response);

            // Return array: [success, status, statusText, ok, url, headers, bodyBytes, null]
            return new object?[] { true, status, statusText, ok, url, responseHeaders, bodyBytes, null };
        }
        catch (HttpRequestException ex)
        {
            return new object?[] { false, 0.0, "", false, "", null, Array.Empty<byte>(), $"fetch failed: {ex.Message}" };
        }
        catch (TaskCanceledException)
        {
            return new object?[] { false, 0.0, "", false, "", null, Array.Empty<byte>(), "fetch: request timeout" };
        }
        catch (UriFormatException ex)
        {
            return new object?[] { false, 0.0, "", false, "", null, Array.Empty<byte>(), $"fetch: Invalid URL - {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new object?[] { false, 0.0, "", false, "", null, Array.Empty<byte>(), $"fetch error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Extracts headers from an HttpResponseMessage as a Dictionary&lt;string, object?&gt;.
    /// </summary>
    public static Dictionary<string, object?> ExtractResponseHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, object?>();

        // Add response headers
        foreach (var header in response.Headers)
        {
            headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
        }

        // Add content headers if content exists
        if (response.Content?.Headers != null)
        {
            foreach (var header in response.Content.Headers)
            {
                headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
            }
        }

        return headers;
    }
}
