using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.LspBridge.Handlers;
using SharpTS.LspBridge.Protocol;
using SharpTS.Tests.LspTests.Helpers;
using Xunit;

namespace SharpTS.Tests.LspTests.Handlers;

/// <summary>
/// Unit tests for GetAttributeInfoHandler.
/// </summary>
public class GetAttributeInfoHandlerTests : IDisposable
{
    private readonly AssemblyReferenceLoader _loader;
    private readonly GetAttributeInfoHandler _handler;

    public GetAttributeInfoHandlerTests()
    {
        _loader = LspBridgeTestHelper.CreateRuntimeLoader();
        _handler = new GetAttributeInfoHandler(_loader);
    }

    public void Dispose()
    {
        _loader.Dispose();
    }

    [Fact]
    public void Handle_ValidAttribute_ReturnsConstructors()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("constructors", out var constructors));
        Assert.Equal(JsonValueKind.Array, constructors.ValueKind);
        Assert.True(constructors.GetArrayLength() > 0, "ObsoleteAttribute should have constructors");
    }

    [Fact]
    public void Handle_ValidAttribute_ReturnsProperties()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("properties", out var properties));
        Assert.Equal(JsonValueKind.Array, properties.ValueKind);
    }

    [Fact]
    public void Handle_TypeNotFound_ReturnsError()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "NonExistent.FakeAttribute" });

        var response = _handler.Handle(request);

        Assert.False(response.Success);
        Assert.Contains("not found", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Handle_MissingTypeName_ReturnsError()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { });

        var response = _handler.Handle(request);

        Assert.False(response.Success);
        Assert.Contains("typeName", response.Message);
    }

    [Fact]
    public void Handle_TypeMapping_ConvertsStringToTypeScript()
    {
        // ObsoleteAttribute has a string parameter
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);

        var constructors = body.GetProperty("constructors");
        var hasStringParam = false;

        foreach (var ctor in constructors.EnumerateArray())
        {
            var parameters = ctor.GetProperty("parameters");
            foreach (var param in parameters.EnumerateArray())
            {
                var type = param.GetProperty("type").GetString();
                if (type == "string")
                {
                    hasStringParam = true;
                    break;
                }
            }
        }

        Assert.True(hasStringParam, "ObsoleteAttribute should have a string parameter (mapped from System.String)");
    }

    [Fact]
    public void Handle_TypeMapping_ConvertsBoolToTypeScript()
    {
        // ObsoleteAttribute has a boolean parameter for IsError
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);

        var constructors = body.GetProperty("constructors");
        var hasBoolParam = false;

        foreach (var ctor in constructors.EnumerateArray())
        {
            var parameters = ctor.GetProperty("parameters");
            foreach (var param in parameters.EnumerateArray())
            {
                var type = param.GetProperty("type").GetString();
                if (type == "boolean")
                {
                    hasBoolParam = true;
                    break;
                }
            }
        }

        Assert.True(hasBoolParam, "ObsoleteAttribute should have a boolean parameter");
    }

    [Fact]
    public void Handle_ReturnsFullName()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("fullName", out var fullName));
        Assert.Equal("System.ObsoleteAttribute", fullName.GetString());
    }

    [Fact]
    public void Handle_WithoutAttributeSuffix_ResolvesWithSuffix()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "System.Obsolete" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.Equal("System.ObsoleteAttribute", body.GetProperty("fullName").GetString());
    }

    [Fact]
    public void Handle_ConstructorHasParameterInfo()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);

        var constructors = body.GetProperty("constructors");
        foreach (var ctor in constructors.EnumerateArray())
        {
            var parameters = ctor.GetProperty("parameters");
            foreach (var param in parameters.EnumerateArray())
            {
                Assert.True(param.TryGetProperty("name", out _), "Parameter should have 'name'");
                Assert.True(param.TryGetProperty("type", out _), "Parameter should have 'type'");
                Assert.True(param.TryGetProperty("isOptional", out _), "Parameter should have 'isOptional'");
            }
        }
    }

    [Fact]
    public void Handle_PropertyHasRequiredInfo()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);

        var properties = body.GetProperty("properties");
        foreach (var prop in properties.EnumerateArray())
        {
            Assert.True(prop.TryGetProperty("name", out _), "Property should have 'name'");
            Assert.True(prop.TryGetProperty("type", out _), "Property should have 'type'");
        }
    }

    [Fact]
    public void Handle_NullArguments_ReturnsError()
    {
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "get-attribute-info",
            Arguments = null
        };

        var response = _handler.Handle(request);

        Assert.False(response.Success);
        Assert.Contains("typeName", response.Message);
    }

    [Fact]
    public void Handle_EmptyTypeName_ReturnsError()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "" });

        var response = _handler.Handle(request);

        Assert.False(response.Success);
        Assert.Contains("typeName", response.Message);
    }

    [Fact]
    public void Handle_AttributeWithDefaultConstructor_ReturnsEmptyParameters()
    {
        // FlagsAttribute has a parameterless constructor
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "System.FlagsAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);

        var constructors = body.GetProperty("constructors");
        var hasParameterlessCtor = false;

        foreach (var ctor in constructors.EnumerateArray())
        {
            var parameters = ctor.GetProperty("parameters");
            if (parameters.GetArrayLength() == 0)
            {
                hasParameterlessCtor = true;
                break;
            }
        }

        Assert.True(hasParameterlessCtor, "FlagsAttribute should have a parameterless constructor");
    }

    [Fact]
    public void Handle_NonAttributeType_StillReturnsInfo()
    {
        // Non-attribute type should still work
        var request = LspBridgeTestHelper.CreateRequest("get-attribute-info", 1, new { typeName = "System.String" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.Equal("System.String", body.GetProperty("fullName").GetString());
    }
}
