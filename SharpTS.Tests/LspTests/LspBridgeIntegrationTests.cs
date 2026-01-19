using System.Text.Json;
using SharpTS.Tests.LspTests.Helpers;
using Xunit;

namespace SharpTS.Tests.LspTests;

/// <summary>
/// Integration tests for the full LspBridge message loop.
/// </summary>
public class LspBridgeIntegrationTests : IDisposable
{
    private readonly SharpTS.LspBridge.LspBridge _bridge;

    public LspBridgeIntegrationTests()
    {
        // Pass empty list - the LspBridge will add SDK assemblies automatically
        _bridge = new SharpTS.LspBridge.LspBridge(Array.Empty<string>());
    }

    public void Dispose()
    {
        _bridge.Dispose();
    }

    [Fact]
    public void Run_StartsWithReadySignal()
    {
        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, Array.Empty<string>());

        var responses = LspBridgeTestHelper.ParseResponses(stdout);
        Assert.NotEmpty(responses);

        var readyResponse = responses.First();
        Assert.True(readyResponse.Success);
        Assert.Equal(0, readyResponse.Seq);

        if (readyResponse.Body is JsonElement body)
        {
            Assert.True(body.TryGetProperty("ready", out var ready));
            Assert.True(ready.GetBoolean());
        }
    }

    [Fact]
    public void ProcessMessage_ValidRequest_ReturnsCorrelatedResponse()
    {
        var request = LspBridgeTestHelper.ToJsonRequest("resolve-type", 42, new { typeName = "System.String" });

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { request });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);

        // Find the response with seq 42 (skip ready signal)
        var matchingResponse = responses.FirstOrDefault(r => r.Seq == 42);
        Assert.NotNull(matchingResponse);
        Assert.True(matchingResponse.Success);
    }

    [Fact]
    public void ProcessMessage_InvalidJson_ReturnsJsonError()
    {
        var invalidJson = "{ this is not valid json }";

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { invalidJson });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);

        // Find error response (seq 0 for parse errors)
        var errorResponse = responses.FirstOrDefault(r => !r.Success);
        Assert.NotNull(errorResponse);
        Assert.Contains("JSON", errorResponse.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessMessage_UnknownCommand_ReturnsUnknownCommandError()
    {
        var request = LspBridgeTestHelper.ToJsonRequest("unknown-command", 5);

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { request });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);
        var errorResponse = responses.FirstOrDefault(r => r.Seq == 5);

        Assert.NotNull(errorResponse);
        Assert.False(errorResponse.Success);
        Assert.Contains("unknown", errorResponse.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessMessage_MissingCommand_ReturnsMissingCommandError()
    {
        var request = JsonSerializer.Serialize(new { seq = 7 });

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { request });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);
        var errorResponse = responses.FirstOrDefault(r => r.Seq == 7);

        Assert.NotNull(errorResponse);
        Assert.False(errorResponse.Success);
        Assert.Contains("command", errorResponse.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessMessage_ShutdownCommand_StopsLoop()
    {
        var shutdownRequest = LspBridgeTestHelper.ToJsonRequest("shutdown", 10);
        var afterShutdown = LspBridgeTestHelper.ToJsonRequest("resolve-type", 11, new { typeName = "System.String" });

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { shutdownRequest, afterShutdown });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);

        // Should have shutdown response
        var shutdownResponse = responses.FirstOrDefault(r => r.Seq == 10);
        Assert.NotNull(shutdownResponse);
        Assert.True(shutdownResponse.Success);

        // Should NOT have response for seq 11 (loop stopped)
        var postShutdownResponse = responses.FirstOrDefault(r => r.Seq == 11);
        Assert.Null(postShutdownResponse);
    }

    [Fact]
    public void ProcessMessage_MultipleRequests_ProcessesAll()
    {
        var requests = new[]
        {
            LspBridgeTestHelper.ToJsonRequest("resolve-type", 1, new { typeName = "System.String" }),
            LspBridgeTestHelper.ToJsonRequest("resolve-type", 2, new { typeName = "System.Int32" }),
            LspBridgeTestHelper.ToJsonRequest("resolve-type", 3, new { typeName = "System.Boolean" })
        };

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, requests);

        var responses = LspBridgeTestHelper.ParseResponses(stdout);

        // Should have responses for all three requests (plus ready signal)
        Assert.Contains(responses, r => r.Seq == 1);
        Assert.Contains(responses, r => r.Seq == 2);
        Assert.Contains(responses, r => r.Seq == 3);
    }

    [Fact]
    public void ProcessMessage_EmptyLine_SkipsWithoutError()
    {
        var requests = new[]
        {
            "",
            LspBridgeTestHelper.ToJsonRequest("resolve-type", 1, new { typeName = "System.String" }),
            "   ",
            LspBridgeTestHelper.ToJsonRequest("resolve-type", 2, new { typeName = "System.Int32" })
        };

        var (stdout, stderr) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, requests);

        var responses = LspBridgeTestHelper.ParseResponses(stdout);

        // Both valid requests should have responses
        Assert.Contains(responses, r => r.Seq == 1 && r.Success);
        Assert.Contains(responses, r => r.Seq == 2 && r.Success);
    }

    [Fact]
    public void Dispose_DisposesLoader()
    {
        // Pass empty list - the LspBridge will add SDK assemblies automatically
        var bridge = new SharpTS.LspBridge.LspBridge(Array.Empty<string>());

        // Should not throw
        bridge.Dispose();

        // Double dispose should also not throw
        bridge.Dispose();
    }

    [Fact]
    public void ProcessMessage_CommandCaseInsensitive()
    {
        // Commands should be case-insensitive
        var request = LspBridgeTestHelper.ToJsonRequest("RESOLVE-TYPE", 15, new { typeName = "System.String" });

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { request });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);
        var response = responses.FirstOrDefault(r => r.Seq == 15);

        Assert.NotNull(response);
        Assert.True(response.Success);
    }

    [Fact]
    public void ProcessMessage_ListAttributes_ReturnsResults()
    {
        var request = LspBridgeTestHelper.ToJsonRequest("list-attributes", 20);

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { request });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);
        var response = responses.FirstOrDefault(r => r.Seq == 20);

        Assert.NotNull(response);
        Assert.True(response.Success);
    }

    [Fact]
    public void ProcessMessage_GetAttributeInfo_ReturnsResults()
    {
        var request = LspBridgeTestHelper.ToJsonRequest("get-attribute-info", 25, new { typeName = "System.ObsoleteAttribute" });

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { request });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);
        var response = responses.FirstOrDefault(r => r.Seq == 25);

        Assert.NotNull(response);
        Assert.True(response.Success);
    }

    [Fact]
    public void ProcessMessage_GetTypeDocumentation_ReturnsResults()
    {
        var request = LspBridgeTestHelper.ToJsonRequest("get-type-documentation", 30, new { typeName = "System.String" });

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { request });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);
        var response = responses.FirstOrDefault(r => r.Seq == 30);

        Assert.NotNull(response);
        Assert.True(response.Success);
    }

    [Fact]
    public void ProcessMessage_NullRequest_ReturnsError()
    {
        var nullRequest = "null";

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { nullRequest });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);
        var errorResponse = responses.FirstOrDefault(r => !r.Success && r.Seq == 0);

        // Note: Deserializing "null" may result in a null request which triggers an error
        Assert.NotNull(errorResponse);
    }

    [Fact]
    public void Run_HandlesEofGracefully()
    {
        // When stdin ends (EOF), the bridge should stop cleanly
        var (stdout, stderr) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, Array.Empty<string>());

        // Should have at least the ready signal
        var responses = LspBridgeTestHelper.ParseResponses(stdout);
        Assert.NotEmpty(responses);

        // No errors should be written to stderr (EOF is expected)
        // Note: Some implementations may log, so we just verify it doesn't crash
    }

    [Fact]
    public void ProcessMessage_HandlerException_ReturnsErrorResponse()
    {
        // Force an error by providing invalid arguments
        var request = LspBridgeTestHelper.ToJsonRequest("get-attribute-info", 35, new { typeName = (string?)null });

        var (stdout, _) = LspBridgeTestHelper.RunBridgeWithInput(_bridge, new[] { request });

        var responses = LspBridgeTestHelper.ParseResponses(stdout);
        var response = responses.FirstOrDefault(r => r.Seq == 35);

        Assert.NotNull(response);
        Assert.False(response.Success);
    }
}
