using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the fetch <c>credentials</c> option and the process-wide cookie jar.
/// </summary>
/// <remarks>
/// Pure-script tests run in both interpreter and compiled modes — the compiled DLL
/// runs in a separate dotnet process, so each compiled test gets a fresh cookie jar
/// without any cross-test state leak.
///
/// Tests that introspect the C# <see cref="SharpTSCookieJar.Instance"/> directly
/// (cross-host isolation, getCookieHeader, clear) only run in interpreter mode —
/// the compiled DLL has its own static <c>_cookieContainer</c> field that is
/// inaccessible from the test process.
///
/// Each interpreter test clears <see cref="SharpTSCookieJar.Instance"/> in the
/// fixture constructor so tests don't leak state.
/// </remarks>
public class FetchCookieTests : IDisposable
{
    private readonly MockHttpServer _server;

    public FetchCookieTests()
    {
        // Force-clear the process-wide jar before every test.
        SharpTSCookieJar.Instance.Clear();

        _server = new MockHttpServer();
        _server.AddSetCookieRoute("/set-session", "session=abc123; Path=/");
        _server.AddSetCookieRoute("/set-multi", "a=1; Path=/", "b=2; Path=/");
        _server.AddSetCookieRoute("/set-expired", "gone=yes; Max-Age=0; Path=/");
        _server.AddSetCookieRoute("/set-httponly", "secret=42; Path=/; HttpOnly");
        _server.AddCookieEchoRoute("/echo-cookie");
        _server.AddSetCookieRedirectRoute("/redirect-with-cookie", "/echo-cookie", "via_redirect=yes; Path=/", 302);
        _server.AddTextRoute("/plain", "ok");
        _server.Start();
    }

    public void Dispose()
    {
        _server.Dispose();
        SharpTSCookieJar.Instance.Clear();
    }

    // ========== 1. Set-Cookie stored, sent on next request ==========

    [Theory, ModeData]
    public void Cookies_StoredAndResent_DefaultCredentials(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-session');
                const res = await fetch('{{_server.BaseUrl}}echo-cookie');
                const text = await res.text();
                console.log(text);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("session=abc123", output);
    }

    // ========== 2. credentials: 'omit' does not send stored cookies ==========

    [Theory, ModeData]
    public void Cookies_OmitDoesNotSendStored(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-session');
                const res = await fetch('{{_server.BaseUrl}}echo-cookie', { credentials: 'omit' });
                const text = await res.text();
                console.log('[' + text + ']');
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[]\n", output);
    }

    // ========== 2b. credentials: 'include' sends stored cookies (same as default) ==========

    [Theory, ModeData]
    public void Cookies_IncludeBehavesLikeSameOriginForSameHost(ExecutionMode mode)
    {
        // Without a top-level realm, 'include' and 'same-origin' both use the
        // with-cookies HttpClient. This documents that explicit 'include' works
        // identically to the default for the common single-host case.
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-session', { credentials: 'include' });
                const res = await fetch('{{_server.BaseUrl}}echo-cookie', { credentials: 'include' });
                const text = await res.text();
                console.log(text);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("session=abc123", output);
    }

    // ========== 3. credentials: 'omit' does not store Set-Cookie ==========

    [Theory, ModeData]
    public void Cookies_OmitDoesNotStoreSetCookie(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-session', { credentials: 'omit' });
                const res = await fetch('{{_server.BaseUrl}}echo-cookie');
                const text = await res.text();
                console.log('[' + text + ']');
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[]\n", output);
    }

    // ========== 4. Cross-host isolation ==========

    [Theory, InterpretedOnlyData]
    public void Cookies_CrossHostIsolation(ExecutionMode mode)
    {
        // Set a cookie for our test server, then verify a different host doesn't get it.
        // We can't easily run two listeners from a single test, so verify via direct
        // CookieContainer query: cookies for example.invalid should be empty.
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-session');
                console.log('done');
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("done\n", output);

        var localhostCookies = SharpTSCookieJar.Instance.GetCookieHeader(_server.BaseUrl);
        var otherHostCookies = SharpTSCookieJar.Instance.GetCookieHeader("http://example.invalid/");
        Assert.Contains("session=abc123", localhostCookies);
        Assert.Equal("", otherHostCookies);
    }

    // ========== 5. Expired cookie removed ==========

    [Theory, ModeData]
    public void Cookies_ExpiredCookieNotSent(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-expired');
                const res = await fetch('{{_server.BaseUrl}}echo-cookie');
                const text = await res.text();
                console.log('[' + text + ']');
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[]\n", output);
    }

    // ========== 6. HttpOnly cookie sent in subsequent requests ==========

    [Theory, ModeData]
    public void Cookies_HttpOnlyCookieSentInSubsequentRequest(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-httponly');
                const res = await fetch('{{_server.BaseUrl}}echo-cookie');
                const text = await res.text();
                console.log(text);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("secret=42", output);
    }

    // ========== 7. Multiple Set-Cookie preserved as array via getSetCookie() ==========

    [Theory, ModeData]
    public void Cookies_GetSetCookieReturnsArray(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}set-multi');
                const cookies = res.headers.getSetCookie();
                console.log(cookies.length);
                console.log(cookies[0]);
                console.log(cookies[1]);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\na=1; Path=/\nb=2; Path=/\n", output);
    }

    // ========== 8. headers.get('set-cookie') returns first ==========

    [Theory, ModeData]
    public void Cookies_HeadersGetReturnsFirstSetCookie(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}set-multi');
                console.log(res.headers.get('set-cookie'));
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a=1; Path=/\n", output);
    }

    // ========== 9. Cookies survive a redirect chain ==========

    [Theory, ModeData]
    public void Cookies_StoredFromRedirectIntermediate(ExecutionMode mode)
    {
        // Hit a redirect that sets a cookie, follow to /echo-cookie which echoes the
        // cookie header back. The cookie set on the redirect response must be sent
        // on the follow-up request.
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}redirect-with-cookie');
                const text = await res.text();
                console.log(text);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("via_redirect=yes", output);
    }

    // ========== 10. cookieJar.clear() removes all cookies ==========

    [Theory, InterpretedOnlyData]
    public void Cookies_ClearJarRemovesAll(ExecutionMode mode)
    {
        // Step 1: store a cookie
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-session');
                console.log('stored');
            }
            test();
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("stored\n", output);

        // Verify stored — both the header view and the raw count
        Assert.Contains("session=abc123", SharpTSCookieJar.Instance.GetCookieHeader(_server.BaseUrl));
        Assert.NotEmpty(SharpTSCookieJar.GlobalContainer.GetAllCookies());

        SharpTSCookieJar.Instance.Clear();

        // Both views must agree the jar is empty after Clear() — not just the
        // header path. The lazy expiry approach used to leave Count > 0 here.
        Assert.Equal("", SharpTSCookieJar.Instance.GetCookieHeader(_server.BaseUrl));
        Assert.Empty(SharpTSCookieJar.GlobalContainer.GetAllCookies());
    }

    // ========== Invalid URL handling — both methods throw, symmetric ==========

    [Theory, ModeData]
    public void Cookies_GetCookiesThrowsOnInvalidUrl(ExecutionMode mode)
    {
        var source = """
            try {
                fetch.cookieJar.getCookies('not a url');
                console.log('no throw');
            } catch (e) {
                const name = (e as any).name ?? '';
                console.log(name === 'TypeError' ? 'typeerror' : 'other:' + name);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("typeerror\n", output);
    }

    [Theory, ModeData]
    public void Cookies_SetCookieThrowsOnInvalidUrl(ExecutionMode mode)
    {
        var source = """
            try {
                fetch.cookieJar.setCookie('foo=bar', 'not a url');
                console.log('no throw');
            } catch (e) {
                const name = (e as any).name ?? '';
                console.log(name === 'TypeError' ? 'typeerror' : 'other:' + name);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("typeerror\n", output);
    }

    // ========== 11. cookieJar.getCookies(url) returns the right header ==========

    [Theory, InterpretedOnlyData]
    public void Cookies_GetCookieHeaderReturnsSentValue(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-session');
            }
            test();
            """;
        TestHarness.Run(source, mode);

        var header = SharpTSCookieJar.Instance.GetCookieHeader(_server.BaseUrl);
        Assert.Equal("session=abc123", header);
    }

    // ========== http.get/http.request cookies ==========

    [Theory, InterpretedOnlyData]
    public void Cookies_HttpModuleSharesJarWithFetch(ExecutionMode mode)
    {
        // Set a cookie via fetch, then read it back via http.get. The ClientRequest (#1043)
        // attaches cookies from the shared jar, so echo-cookie reflects the fetch-set cookie.
        // Interpreted-only: the compiled http client still routes through fetch (which shares
        // the same jar — covered by the dual-mode fetch cookie tests).
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                async function test(): Promise<void> {
                    await fetch('{{_server.BaseUrl}}set-session');
                    http.get('{{_server.BaseUrl}}echo-cookie', (res: any) => {
                        let text = '';
                        res.on('data', (c: any) => { text += c.toString(); });
                        res.on('end', () => { console.log(text); });
                    });
                }
                test();
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("session=abc123", output);
    }

    [Theory, InterpretedOnlyData]
    public void Cookies_HttpRequestStoresAndSendsCookies(ExecutionMode mode)
    {
        // Pure http.request flow: set via http.request, read via http.request — proves the
        // ClientRequest (#1043) both stores Set-Cookie and re-sends from the shared jar.
        // Interpreted-only (see the ClientRequest note above).
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const setReq: any = http.request('{{_server.BaseUrl}}set-session', (res1: any) => {
                    res1.on('data', () => {});
                    res1.on('end', () => {
                        const getReq: any = http.request('{{_server.BaseUrl}}echo-cookie', (res2: any) => {
                            let text = '';
                            res2.on('data', (c: any) => { text += c.toString(); });
                            res2.on('end', () => { console.log(text); });
                        });
                        getReq.end();
                    });
                });
                setReq.end();
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("session=abc123", output);
    }

    // ========== 13. fetch.cookieJar.getCookies(url) from script (both modes) ==========

    [Theory, ModeData]
    public void Cookies_FetchCookieJarGetCookies_FromScript(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-session');
                const header = fetch.cookieJar.getCookies('{{_server.BaseUrl}}');
                console.log(header);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("session=abc123\n", output);
    }

    // ========== 14. fetch.cookieJar.setCookie() from script ==========

    [Theory, ModeData]
    public void Cookies_FetchCookieJarSetCookie_FromScript(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                fetch.cookieJar.setCookie('manual=injected; Path=/', '{{_server.BaseUrl}}');
                const res = await fetch('{{_server.BaseUrl}}echo-cookie');
                const text = await res.text();
                console.log(text);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("manual=injected", output);
    }

    // ========== 15. fetch.cookieJar.clear() from script ==========

    [Theory, ModeData]
    public void Cookies_FetchCookieJarClear_FromScript(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                await fetch('{{_server.BaseUrl}}set-session');
                fetch.cookieJar.clear();
                const res = await fetch('{{_server.BaseUrl}}echo-cookie');
                const text = await res.text();
                console.log('[' + text + ']');
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[]\n", output);
    }

    // ========== 12. Concurrent fetches don't race the jar ==========

    [Theory, InterpretedOnlyData]
    public void Cookies_ConcurrentFetchesDoNotRaceJar(ExecutionMode mode)
    {
        // Fire 20 parallel fetches that each set the same cookie, then check that
        // the jar has exactly one entry. CookieContainer is documented as
        // thread-safe; this is a smoke test.
        var source = $$"""
            async function test(): Promise<void> {
                const promises: Promise<unknown>[] = [];
                for (let i = 0; i < 20; i++) {
                    promises.push(fetch('{{_server.BaseUrl}}set-session'));
                }
                await Promise.all(promises);
                console.log('all done');
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("all done\n", output);

        // Should have exactly one session cookie
        var header = SharpTSCookieJar.Instance.GetCookieHeader(_server.BaseUrl);
        Assert.Equal("session=abc123", header);
    }
}
