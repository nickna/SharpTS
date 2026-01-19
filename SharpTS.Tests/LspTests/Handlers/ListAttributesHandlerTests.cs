using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.LspBridge.Handlers;
using SharpTS.LspBridge.Protocol;
using SharpTS.Tests.LspTests.Helpers;
using Xunit;

namespace SharpTS.Tests.LspTests.Handlers;

/// <summary>
/// Unit tests for ListAttributesHandler.
/// </summary>
public class ListAttributesHandlerTests : IDisposable
{
    private readonly AssemblyReferenceLoader _loader;
    private readonly ListAttributesHandler _handler;

    public ListAttributesHandlerTests()
    {
        _loader = LspBridgeTestHelper.CreateRuntimeLoader();
        _handler = new ListAttributesHandler(_loader);
    }

    public void Dispose()
    {
        _loader.Dispose();
    }

    [Fact]
    public void Handle_NoFilter_ReturnsAttributes()
    {
        var request = LspBridgeTestHelper.CreateRequest("list-attributes", 1);

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        Assert.NotNull(response.Body);

        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("attributes", out var attributes));
        Assert.Equal(JsonValueKind.Array, attributes.ValueKind);
    }

    [Fact]
    public void Handle_WithFilter_ReturnsFilteredResults()
    {
        var request = LspBridgeTestHelper.CreateRequest("list-attributes", 1, new { filter = "Obsolete" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        Assert.NotNull(response.Body);

        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("attributes", out var attributes));

        // All results should contain "Obsolete" (case-insensitive)
        foreach (var attr in attributes.EnumerateArray())
        {
            var name = attr.GetProperty("name").GetString() ?? "";
            var fullName = attr.GetProperty("fullName").GetString() ?? "";
            Assert.True(
                name.Contains("Obsolete", StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains("Obsolete", StringComparison.OrdinalIgnoreCase),
                $"Attribute {fullName} should contain 'Obsolete'");
        }
    }

    [Fact]
    public void Handle_FilterCaseInsensitive_ReturnsMatches()
    {
        var request = LspBridgeTestHelper.CreateRequest("list-attributes", 1, new { filter = "obsolete" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("attributes", out var attributes));
        Assert.True(attributes.GetArrayLength() > 0, "Should find Obsolete attribute with lowercase filter");
    }

    [Fact]
    public void Handle_FilterNoMatches_ReturnsEmptyList()
    {
        var request = LspBridgeTestHelper.CreateRequest("list-attributes", 1, new { filter = "XyzNonExistentAttribute123" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("attributes", out var attributes));
        Assert.Equal(0, attributes.GetArrayLength());
    }

    [Fact]
    public void Handle_MultipleCalls_UsesCachedResults()
    {
        // First call
        var request1 = LspBridgeTestHelper.CreateRequest("list-attributes", 1);
        var response1 = _handler.Handle(request1);

        // Second call
        var request2 = LspBridgeTestHelper.CreateRequest("list-attributes", 2);
        var response2 = _handler.Handle(request2);

        Assert.True(response1.Success);
        Assert.True(response2.Success);

        // Both should return the same count (cached)
        var body1 = LspBridgeTestHelper.ToJsonElement(response1);
        var body2 = LspBridgeTestHelper.ToJsonElement(response2);

        var attrs1 = body1.GetProperty("attributes");
        var attrs2 = body2.GetProperty("attributes");
        Assert.Equal(attrs1.GetArrayLength(), attrs2.GetArrayLength());
    }

    [Fact]
    public void Handle_AttributesAreSorted()
    {
        var request = LspBridgeTestHelper.CreateRequest("list-attributes", 1);

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        var attributes = body.GetProperty("attributes");

        var names = new List<string>();
        foreach (var attr in attributes.EnumerateArray())
        {
            names.Add(attr.GetProperty("name").GetString() ?? "");
        }

        var sortedNames = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sortedNames, names);
    }

    [Fact]
    public void Handle_AttributeHasRequiredProperties()
    {
        var request = LspBridgeTestHelper.CreateRequest("list-attributes", 1);

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        var attributes = body.GetProperty("attributes");

        if (attributes.GetArrayLength() > 0)
        {
            var firstAttr = attributes[0];

            Assert.True(firstAttr.TryGetProperty("name", out _), "Attribute should have 'name' property");
            Assert.True(firstAttr.TryGetProperty("fullName", out _), "Attribute should have 'fullName' property");
            Assert.True(firstAttr.TryGetProperty("namespace", out _), "Attribute should have 'namespace' property");
            Assert.True(firstAttr.TryGetProperty("assembly", out _), "Attribute should have 'assembly' property");
        }
    }

    [Fact]
    public void Handle_AttributeNameStripsAttributeSuffix()
    {
        var request = LspBridgeTestHelper.CreateRequest("list-attributes", 1, new { filter = "Obsolete" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        var attributes = body.GetProperty("attributes");

        foreach (var attr in attributes.EnumerateArray())
        {
            var name = attr.GetProperty("name").GetString();
            var fullName = attr.GetProperty("fullName").GetString();

            // Name should NOT end with "Attribute" (suffix is stripped)
            Assert.False(name?.EndsWith("Attribute"), $"Name '{name}' should not end with 'Attribute'");
            // But FullName should contain "Attribute"
            Assert.True(fullName?.EndsWith("Attribute"), $"FullName '{fullName}' should end with 'Attribute'");
        }
    }

    [Fact]
    public void Handle_EmptyFilter_TreatedAsNoFilter()
    {
        var requestNoFilter = LspBridgeTestHelper.CreateRequest("list-attributes", 1);
        var requestEmptyFilter = LspBridgeTestHelper.CreateRequest("list-attributes", 2, new { filter = "" });

        var responseNoFilter = _handler.Handle(requestNoFilter);
        var responseEmptyFilter = _handler.Handle(requestEmptyFilter);

        Assert.True(responseNoFilter.Success);
        Assert.True(responseEmptyFilter.Success);

        var bodyNoFilter = LspBridgeTestHelper.ToJsonElement(responseNoFilter);
        var bodyEmptyFilter = LspBridgeTestHelper.ToJsonElement(responseEmptyFilter);

        var countNoFilter = bodyNoFilter.GetProperty("attributes").GetArrayLength();
        var countEmptyFilter = bodyEmptyFilter.GetProperty("attributes").GetArrayLength();

        Assert.Equal(countNoFilter, countEmptyFilter);
    }

    [Fact]
    public void Handle_NullArguments_ReturnsAllAttributes()
    {
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "list-attributes",
            Arguments = null
        };

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("attributes", out _));
    }
}
