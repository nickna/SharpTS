using SharpTS.LspBridge.Protocol;
using Xunit;

namespace SharpTS.Tests.LspTests.Protocol;

/// <summary>
/// Unit tests for BridgeResponse class.
/// </summary>
public class BridgeResponseTests
{
    [Fact]
    public void Ok_WithBody_CreatesSuccessResponse()
    {
        var body = new { name = "test", value = 42 };

        var response = BridgeResponse.Ok(body);

        Assert.True(response.Success);
        Assert.Null(response.Message);
        Assert.NotNull(response.Body);
    }

    [Fact]
    public void Ok_WithNullBody_CreatesSuccessResponse()
    {
        var response = BridgeResponse.Ok(null);

        Assert.True(response.Success);
        Assert.Null(response.Message);
        Assert.Null(response.Body);
    }

    [Fact]
    public void Ok_WithoutBody_CreatesSuccessResponse()
    {
        var response = BridgeResponse.Ok();

        Assert.True(response.Success);
        Assert.Null(response.Message);
        Assert.Null(response.Body);
    }

    [Fact]
    public void Error_WithMessage_CreatesErrorResponse()
    {
        var response = BridgeResponse.Error("Something went wrong");

        Assert.False(response.Success);
        Assert.Equal("Something went wrong", response.Message);
        Assert.Null(response.Body);
    }

    [Fact]
    public void Error_WithEmptyMessage_CreatesErrorResponse()
    {
        var response = BridgeResponse.Error("");

        Assert.False(response.Success);
        Assert.Equal("", response.Message);
    }

    [Fact]
    public void Seq_CanBeSet_ForCorrelation()
    {
        var response = BridgeResponse.Ok(new { test = true });
        response.Seq = 42;

        Assert.Equal(42, response.Seq);
    }

    [Fact]
    public void Seq_DefaultsToZero()
    {
        var response = BridgeResponse.Ok();

        Assert.Equal(0, response.Seq);
    }

    [Fact]
    public void Ok_WithComplexBody_PreservesBody()
    {
        var body = new
        {
            exists = true,
            fullName = "System.String",
            isAttribute = false,
            isAbstract = false,
            isSealed = true,
            assembly = "System.Runtime"
        };

        var response = BridgeResponse.Ok(body);

        Assert.True(response.Success);
        Assert.NotNull(response.Body);
        // Body is preserved as the anonymous type
        dynamic dynamicBody = response.Body;
        Assert.True(dynamicBody.exists);
        Assert.Equal("System.String", dynamicBody.fullName);
    }

    [Fact]
    public void Ok_WithListBody_PreservesBody()
    {
        var body = new
        {
            attributes = new[]
            {
                new { name = "Obsolete", fullName = "System.ObsoleteAttribute" },
                new { name = "Serializable", fullName = "System.SerializableAttribute" }
            }
        };

        var response = BridgeResponse.Ok(body);

        Assert.True(response.Success);
        Assert.NotNull(response.Body);
    }

    [Fact]
    public void Error_DoesNotHaveBody()
    {
        var response = BridgeResponse.Error("Error message");

        Assert.False(response.Success);
        Assert.Null(response.Body);
    }

    [Fact]
    public void Success_IsReadOnly_AfterCreation()
    {
        var okResponse = BridgeResponse.Ok();
        var errorResponse = BridgeResponse.Error("test");

        // Success is init-only, so these values are fixed after creation
        Assert.True(okResponse.Success);
        Assert.False(errorResponse.Success);
    }

    [Fact]
    public void Message_IsReadOnly_AfterCreation()
    {
        var response = BridgeResponse.Error("initial message");

        // Message is init-only
        Assert.Equal("initial message", response.Message);
    }

    [Fact]
    public void Body_IsReadOnly_AfterCreation()
    {
        var body = new { value = 1 };
        var response = BridgeResponse.Ok(body);

        // Body is init-only
        Assert.Same(body, response.Body);
    }
}
