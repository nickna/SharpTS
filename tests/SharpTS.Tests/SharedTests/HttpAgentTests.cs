using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for http.Agent - connection pooling agent with full Node.js API surface.
/// </summary>
public class HttpAgentTests
{
    #region globalAgent

    [Theory, ModeData]
    public void HttpGlobalAgent_IsObject(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                console.log(typeof http.globalAgent);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void HttpGlobalAgent_KeepAlive_IsTrue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                console.log(http.globalAgent.keepAlive);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void HttpGlobalAgent_MaxSockets_IsInfinity(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                console.log(http.globalAgent.maxSockets);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("Infinity\n", output);
    }

    [Theory, ModeData]
    public void HttpGlobalAgent_AllProperties(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const a = http.globalAgent;
                console.log(a.keepAlive);
                console.log(a.keepAliveMsecs);
                console.log(a.maxSockets);
                console.log(a.maxTotalSockets);
                console.log(a.maxFreeSockets);
                console.log(a.timeout);
                console.log(a.scheduling);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n1000\nInfinity\nInfinity\n256\n0\nlifo\n", output);
    }

    [Theory, ModeData]
    public void HttpGlobalAgent_Destroy(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                console.log(typeof http.globalAgent.destroy);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void HttpGlobalAgent_GetName(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const name = http.globalAgent.getName({ host: 'example.com', port: 443 });
                console.log(name);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("example.com:443::\n", output);
    }

    [Theory, ModeData]
    public void HttpGlobalAgent_Sockets_IsObject(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                console.log(typeof http.globalAgent.sockets);
                console.log(typeof http.globalAgent.freeSockets);
                console.log(typeof http.globalAgent.requests);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("object\nobject\nobject\n", output);
    }

    #endregion

    #region Agent constructor

    [Theory, ModeData]
    public void Agent_Constructor_Exists(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                console.log(typeof http.Agent);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void Agent_Constructor_NoArgs(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const agent = new http.Agent();
                console.log(typeof agent);
                console.log(agent.keepAlive);
                console.log(agent.maxSockets);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("object\nfalse\nInfinity\n", output);
    }

    [Theory, ModeData]
    public void Agent_Constructor_WithKeepAlive(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const agent = new http.Agent({ keepAlive: true });
                console.log(agent.keepAlive);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Agent_Constructor_WithAllOptions(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const agent = new http.Agent({
                    keepAlive: true,
                    keepAliveMsecs: 5000,
                    maxSockets: 10,
                    maxTotalSockets: 50,
                    maxFreeSockets: 5,
                    timeout: 30000
                });
                console.log(agent.keepAlive);
                console.log(agent.keepAliveMsecs);
                console.log(agent.maxSockets);
                console.log(agent.maxTotalSockets);
                console.log(agent.maxFreeSockets);
                console.log(agent.timeout);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n5000\n10\n50\n5\n30000\n", output);
    }

    #endregion

    #region Agent instance methods (interpreter only - use GetMember dispatch)

    [Theory, ModeData]
    public void Agent_GetName_Default(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const agent = new http.Agent();
                console.log(agent.getName());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("localhost:80::\n", output);
    }

    [Theory, ModeData]
    public void Agent_GetName_WithOptions(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const agent = new http.Agent();
                console.log(agent.getName({ host: 'api.example.com', port: 8080 }));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("api.example.com:8080::\n", output);
    }

    [Theory, ModeData]
    public void Agent_Destroy(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const agent = new http.Agent({ keepAlive: true });
                agent.destroy();
                console.log('destroyed');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("destroyed\n", output);
    }

    #endregion

    #region Named import

    [Theory, ModeData]
    public void Agent_NamedImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Agent } from 'http';
                const agent = new Agent({ keepAlive: true, maxSockets: 5 });
                console.log(agent.keepAlive);
                console.log(agent.maxSockets);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n5\n", output);
    }

    #endregion

    #region Property mutation

    [Theory, ModeData]
    public void Agent_PropertyMutation_MaxSockets(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const agent = new http.Agent();
                console.log(agent.maxSockets);
                agent.maxSockets = 10;
                console.log(agent.maxSockets);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("Infinity\n10\n", output);
    }

    [Theory, ModeData]
    public void Agent_PropertyMutation_KeepAlive(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const agent = new http.Agent();
                console.log(agent.keepAlive);
                agent.keepAlive = true;
                console.log(agent.keepAlive);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\ntrue\n", output);
    }

    #endregion

    #region Agent as request option (compatibility)

    [Theory, ModeData]
    public void Agent_InRequestOptions_DoesNotThrow(ExecutionMode mode)
    {
        // Verify that passing agent in request options doesn't cause errors
        // (even though we don't use it for actual connection pooling)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const agent = new http.Agent({ keepAlive: true });
                console.log(typeof agent);
                console.log(agent.keepAlive);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("object\ntrue\n", output);
    }

    #endregion
}
