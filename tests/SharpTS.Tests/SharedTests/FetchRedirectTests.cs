using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the fetch() redirect option: "follow" (default), "manual", "error".
/// </summary>
public class FetchRedirectTests : IDisposable
{
    private readonly MockHttpServer _server;

    public FetchRedirectTests()
    {
        _server = new MockHttpServer();
        _server.AddTextRoute("/target", "redirected content");
        _server.AddRedirectRoute("/redirect", "/target", 302);
        _server.AddRedirectRoute("/redirect301", "/target", 301);
        _server.AddStatusRoute("/status/200", 200, "OK");
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    #region redirect: "follow" (default)

    [Theory, ModeData]
    public void Fetch_RedirectFollow_Default_FollowsRedirect(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}redirect');
                console.log(res.status);
                const text = await res.text();
                console.log(text);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("200\nredirected content\n", output);
    }

    [Theory, ModeData]
    public void Fetch_RedirectFollow_Explicit_FollowsRedirect(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}redirect', { redirect: 'follow' });
                console.log(res.status);
                const text = await res.text();
                console.log(text);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("200\nredirected content\n", output);
    }

    #endregion

    #region redirect: "manual"

    [Theory, ModeData]
    public void Fetch_RedirectManual_Returns302(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}redirect', { redirect: 'manual' });
                console.log(res.status);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("302\n", output);
    }

    [Theory, ModeData]
    public void Fetch_RedirectManual_Returns301(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}redirect301', { redirect: 'manual' });
                console.log(res.status);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("301\n", output);
    }

    [Theory, ModeData]
    public void Fetch_RedirectManual_OkIsFalse(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}redirect', { redirect: 'manual' });
                console.log(res.ok);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Fetch_RedirectManual_NonRedirectWorksNormally(ExecutionMode mode)
    {
        // When redirect is "manual" but the response is not a redirect, it should work normally
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}status/200', { redirect: 'manual' });
                console.log(res.status);
                console.log(res.ok);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("200\ntrue\n", output);
    }

    #endregion

    #region Response properties

    [Theory, ModeData]
    public void Fetch_Response_TypeProperty(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}status/200');
                console.log(res.type);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("basic\n", output);
    }

    [Theory, ModeData]
    public void Fetch_Response_RedirectedProperty_NoRedirect(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}status/200');
                console.log(res.redirected);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region redirect: "error"

    [Theory, ModeData]
    public void Fetch_RedirectError_ThrowsOnRedirect(ExecutionMode mode)
    {
        var source = $$"""
            async function test(): Promise<void> {
                try {
                    await fetch('{{_server.BaseUrl}}redirect', { redirect: 'error' });
                    console.log('should not reach');
                } catch (e) {
                    console.log('caught');
                }
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\n", output);
    }

    [Theory, ModeData]
    public void Fetch_RedirectError_NonRedirectWorksNormally(ExecutionMode mode)
    {
        // When redirect is "error" but the response is not a redirect, it should work normally
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}status/200', { redirect: 'error' });
                console.log(res.status);
                console.log(res.ok);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("200\ntrue\n", output);
    }

    #endregion
}
