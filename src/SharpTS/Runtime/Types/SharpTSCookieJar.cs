using System.Net;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Process-wide cookie store backing the WHATWG fetch <c>credentials</c> option.
/// </summary>
/// <remarks>
/// Wraps a <see cref="System.Net.CookieContainer"/> — which already implements
/// RFC 6265 parsing, domain/path matching, expiry, and Secure/HttpOnly flags —
/// and exposes a small JS-facing API:
///
/// <code>
/// fetch.cookieJar.getCookies(url)
/// fetch.cookieJar.setCookie(cookieString, url)
/// fetch.cookieJar.clear()
/// </code>
///
/// The container is shared between all with-cookies <c>HttpClient</c> instances
/// in both the interpreter and the compiled-mode emitted runtime, so cookies
/// flow naturally between requests.
///
/// Persistence is process-only: nothing is written to disk.
///
/// Implements <see cref="ISharpTSPropertyAccessor"/> rather than
/// <see cref="ITypeCategorized"/> so that property access (e.g.
/// <c>fetch.cookieJar.getCookies</c>) routes through the interpreter fallback path
/// and finds our member binders.
/// </remarks>
public class SharpTSCookieJar : ISharpTSPropertyAccessor
{
    /// <summary>
    /// The shared, process-wide cookie container. This is the canonical jar that
    /// the with-cookies HttpClient handlers point at.
    /// </summary>
    public static CookieContainer GlobalContainer { get; } = new CookieContainer();

    /// <summary>The singleton JS-facing wrapper.</summary>
    public static SharpTSCookieJar Instance { get; } = new SharpTSCookieJar();

    private SharpTSCookieJar() { }

    /// <summary>
    /// Returns the value of the <c>Cookie:</c> header that would be sent for the
    /// given URL, given the current jar contents. Throws <c>TypeError</c> on invalid URL.
    /// </summary>
    /// <remarks>
    /// Mirrors WHATWG fetch's "throw on invalid URL" semantics for both this and
    /// <see cref="SetCookie"/> so the two methods don't have asymmetric error handling.
    /// </remarks>
    public string GetCookieHeader(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ThrowException(new SharpTSTypeError($"Invalid URL: {url}"));
        }
        return GlobalContainer.GetCookieHeader(uri);
    }

    /// <summary>
    /// Parses a <c>Set-Cookie</c>-style cookie string and stores it as if it had
    /// been received in a response from the given URL. Throws <c>TypeError</c> on invalid URL.
    /// </summary>
    public void SetCookie(string cookieString, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ThrowException(new SharpTSTypeError($"Invalid URL: {url}"));
        }
        GlobalContainer.SetCookies(uri, cookieString);
    }

    /// <summary>Removes all cookies from the jar.</summary>
    /// <remarks>
    /// <see cref="CookieContainer"/> has no public <c>Clear</c> API and we can't swap
    /// the singleton — existing <see cref="System.Net.Http.HttpClientHandler"/>
    /// instances hold the original reference. <see cref="CookieContainer.GetAllCookies"/>
    /// returns a snapshot, so clearing it does not affect the container either.
    ///
    /// The reliable workaround: mark every cookie as expired, collect their domains,
    /// then call <see cref="CookieContainer.GetCookieHeader(Uri)"/> for each domain.
    /// That call walks the domain table and drops expired cookies in place — after
    /// it returns, <c>Count</c> is zero.
    /// </remarks>
    public void Clear()
    {
        var all = GlobalContainer.GetAllCookies();
        if (all.Count == 0) return;

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Cookie c in all)
        {
            c.Expired = true;
            domains.Add(c.Domain);
        }

        // Trigger the sweep on each known domain. Errors are swallowed because a
        // cookie with a malformed/synthetic domain shouldn't block the rest from
        // being cleared.
        foreach (var domain in domains)
        {
            var host = domain.TrimStart('.');
            if (string.IsNullOrEmpty(host)) continue;
            try
            {
                GlobalContainer.GetCookieHeader(new Uri($"http://{host}/"));
            }
            catch
            {
                // ignored — sweep is best-effort
            }
        }
    }

    /// <summary>
    /// Gets a property (method binder) by name. Returns null for unknown names so the
    /// interpreter fallback can short-circuit gracefully.
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            "getCookies" => BuiltInMethod.CreateV2("getCookies", 1, (_, _, args) =>
            {
                var url = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "" : "";
                return RuntimeValue.FromBoxed(GetCookieHeader(url));
            }),
            "setCookie" => BuiltInMethod.CreateV2("setCookie", 2, (_, _, args) =>
            {
                var cookie = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "" : "";
                var url = args.Length > 1 ? args[1].ToObject()?.ToString() ?? "" : "";
                SetCookie(cookie, url);
                return RuntimeValue.Undefined;
            }),
            "clear" => BuiltInMethod.CreateV2("clear", 0, (_, _, _) =>
            {
                Clear();
                return RuntimeValue.Undefined;
            }),
            _ => null
        };
    }

    /// <summary>
    /// The cookie jar is read-only from the script side; setters are silently ignored
    /// to match JS non-strict-mode semantics for built-in objects.
    /// </summary>
    public void SetProperty(string name, object? value) { }

    public bool HasProperty(string name) => name is "getCookies" or "setCookie" or "clear";

    public IEnumerable<string> PropertyNames
    {
        get
        {
            yield return "getCookies";
            yield return "setCookie";
            yield return "clear";
        }
    }

    public override string ToString() => "CookieJar {}";
}
