using System.Text.Json;
using SharpTS.LspBridge.Protocol;
using Xunit;

namespace SharpTS.Tests.LspTests.Protocol;

/// <summary>
/// Unit tests for BridgeRequest class.
/// </summary>
public class BridgeRequestTests
{
    [Fact]
    public void GetStringArgument_ValidArg_ReturnsValue()
    {
        var json = JsonDocument.Parse("""{"typeName":"System.String"}""");
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "resolve-type",
            Arguments = json.RootElement
        };

        var result = request.GetStringArgument("typeName");

        Assert.Equal("System.String", result);
    }

    [Fact]
    public void GetStringArgument_MissingArg_ReturnsNull()
    {
        var json = JsonDocument.Parse("""{"otherArg":"value"}""");
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "resolve-type",
            Arguments = json.RootElement
        };

        var result = request.GetStringArgument("typeName");

        Assert.Null(result);
    }

    [Fact]
    public void GetStringArgument_NullArguments_ReturnsNull()
    {
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "resolve-type",
            Arguments = null
        };

        var result = request.GetStringArgument("typeName");

        Assert.Null(result);
    }

    [Fact]
    public void GetStringArgument_NonStringValue_ReturnsNull()
    {
        var json = JsonDocument.Parse("""{"count":42}""");
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "test",
            Arguments = json.RootElement
        };

        var result = request.GetStringArgument("count");

        Assert.Null(result);
    }

    [Fact]
    public void GetArgument_ValidTypedArg_ReturnsDeserializedValue()
    {
        var json = JsonDocument.Parse("""{"items":["a","b","c"]}""");
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "test",
            Arguments = json.RootElement
        };

        var result = request.GetArgument<List<string>>("items");

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0]);
        Assert.Equal("b", result[1]);
        Assert.Equal("c", result[2]);
    }

    [Fact]
    public void GetArgument_MissingArg_ReturnsDefault()
    {
        var json = JsonDocument.Parse("""{"otherArg":"value"}""");
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "test",
            Arguments = json.RootElement
        };

        var result = request.GetArgument<int>("count");

        Assert.Equal(0, result);
    }

    [Fact]
    public void GetArgument_NullArguments_ReturnsDefault()
    {
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "test",
            Arguments = null
        };

        var result = request.GetArgument<string>("typeName");

        Assert.Null(result);
    }

    [Fact]
    public void GetArgument_IntegerValue_ReturnsInt()
    {
        var json = JsonDocument.Parse("""{"count":42}""");
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "test",
            Arguments = json.RootElement
        };

        var result = request.GetArgument<int>("count");

        Assert.Equal(42, result);
    }

    [Fact]
    public void GetArgument_BooleanValue_ReturnsBool()
    {
        var json = JsonDocument.Parse("""{"enabled":true}""");
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "test",
            Arguments = json.RootElement
        };

        var result = request.GetArgument<bool>("enabled");

        Assert.True(result);
    }

    [Fact]
    public void GetArgument_ComplexObject_ReturnsDeserialized()
    {
        var json = JsonDocument.Parse("""{"config":{"name":"test","value":123}}""");
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "test",
            Arguments = json.RootElement
        };

        var result = request.GetArgument<TestConfig>("config");

        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal(123, result.Value);
    }

    [Fact]
    public void Seq_DefaultsToZero()
    {
        var request = new BridgeRequest { Command = "test" };

        Assert.Equal(0, request.Seq);
    }

    [Fact]
    public void Command_DefaultsToEmptyString()
    {
        var request = new BridgeRequest();

        Assert.Equal("", request.Command);
    }

    [Fact]
    public void Arguments_DefaultsToNull()
    {
        var request = new BridgeRequest { Seq = 1, Command = "test" };

        Assert.Null(request.Arguments);
    }

    private class TestConfig
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public int Value { get; set; }
    }
}
