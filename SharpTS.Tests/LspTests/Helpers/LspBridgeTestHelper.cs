using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.LspBridge.Protocol;

namespace SharpTS.Tests.LspTests.Helpers;

/// <summary>
/// Helper methods for testing LspBridge components.
/// </summary>
public static class LspBridgeTestHelper
{
    /// <summary>
    /// Lock object for console redirection tests.
    /// </summary>
    public static readonly object ConsoleLock = new();

    /// <summary>
    /// Creates an AssemblyReferenceLoader with runtime assemblies for testing.
    /// We need to pass at least one assembly as a "user" assembly so that
    /// GetAllPublicTypes() has assemblies to scan.
    /// </summary>
    public static AssemblyReferenceLoader CreateRuntimeLoader()
    {
        // Find the SDK reference assemblies path
        var sdkPath = SharpTS.Compilation.SdkResolver.FindReferenceAssembliesPath();
        if (sdkPath != null && Directory.Exists(sdkPath))
        {
            // Pass a few key SDK assemblies as "user" assemblies for testing
            // Don't pass sdkPath as second parameter since that would cause duplicates
            var assemblies = new List<string>();
            var keyAssemblies = new[] { "System.Runtime.dll", "System.Console.dll" };
            foreach (var name in keyAssemblies)
            {
                var path = Path.Combine(sdkPath, name);
                if (File.Exists(path))
                    assemblies.Add(path);
            }
            if (assemblies.Count > 0)
                return new AssemblyReferenceLoader(assemblies);
        }

        // Fallback: use runtime assemblies
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimeDir != null)
        {
            var assemblies = new List<string>();
            var keyAssemblies = new[] { "System.Runtime.dll", "System.Console.dll" };
            foreach (var name in keyAssemblies)
            {
                var path = Path.Combine(runtimeDir, name);
                if (File.Exists(path))
                    assemblies.Add(path);
            }
            if (assemblies.Count > 0)
                return new AssemblyReferenceLoader(assemblies);
        }

        // Last resort: empty list with fallback resolution
        return new AssemblyReferenceLoader(Array.Empty<string>());
    }

    /// <summary>
    /// Creates a BridgeRequest with the specified command and arguments.
    /// </summary>
    public static BridgeRequest CreateRequest(string command, int seq = 1, object? args = null)
    {
        JsonElement? arguments = null;
        if (args != null)
        {
            var json = JsonSerializer.Serialize(args);
            arguments = JsonDocument.Parse(json).RootElement;
        }

        return new BridgeRequest
        {
            Seq = seq,
            Command = command,
            Arguments = arguments
        };
    }

    /// <summary>
    /// Creates a BridgeRequest with string arguments dictionary.
    /// </summary>
    public static BridgeRequest CreateRequest(string command, int seq, Dictionary<string, string> args)
    {
        var json = JsonSerializer.Serialize(args);
        var arguments = JsonDocument.Parse(json).RootElement;

        return new BridgeRequest
        {
            Seq = seq,
            Command = command,
            Arguments = arguments
        };
    }

    /// <summary>
    /// Runs the LspBridge with the given input lines and captures output.
    /// </summary>
    public static (string stdout, string stderr) RunBridgeWithInput(
        SharpTS.LspBridge.LspBridge bridge,
        IEnumerable<string> inputLines)
    {
        lock (ConsoleLock)
        {
            var originalIn = Console.In;
            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                var input = string.Join(Environment.NewLine, inputLines);
                using var inputReader = new StringReader(input);
                using var outputWriter = new StringWriter();
                using var errorWriter = new StringWriter();

                Console.SetIn(inputReader);
                Console.SetOut(outputWriter);
                Console.SetError(errorWriter);

                bridge.Run();

                return (outputWriter.ToString(), errorWriter.ToString());
            }
            finally
            {
                Console.SetIn(originalIn);
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }

    /// <summary>
    /// Parses JSON response lines from bridge output.
    /// </summary>
    public static List<BridgeResponse> ParseResponses(string output)
    {
        var responses = new List<BridgeResponse>();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var response = JsonSerializer.Deserialize<BridgeResponse>(line, options);
                if (response != null)
                    responses.Add(response);
            }
            catch
            {
                // Skip lines that aren't valid JSON
            }
        }

        return responses;
    }

    /// <summary>
    /// Creates a JSON request string.
    /// </summary>
    public static string ToJsonRequest(string command, int seq = 1, object? args = null)
    {
        var request = new
        {
            seq,
            command,
            arguments = args
        };
        return JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Gets the body of a response as a typed object.
    /// </summary>
    public static T? GetResponseBody<T>(BridgeResponse response) where T : class
    {
        if (response.Body is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        return response.Body as T;
    }

    /// <summary>
    /// Gets a property value from the response body JsonElement.
    /// </summary>
    public static T? GetBodyProperty<T>(BridgeResponse response, string propertyName)
    {
        if (response.Body is JsonElement element && element.TryGetProperty(propertyName, out var prop))
        {
            return JsonSerializer.Deserialize<T>(prop.GetRawText());
        }
        return default;
    }

    /// <summary>
    /// Gets a boolean property from the response body.
    /// </summary>
    public static bool? GetBodyBool(BridgeResponse response, string propertyName)
    {
        if (response.Body is JsonElement element && element.TryGetProperty(propertyName, out var prop))
        {
            return prop.GetBoolean();
        }
        return null;
    }

    /// <summary>
    /// Gets a string property from the response body.
    /// </summary>
    public static string? GetBodyString(BridgeResponse response, string propertyName)
    {
        if (response.Body is JsonElement element && element.TryGetProperty(propertyName, out var prop))
        {
            return prop.GetString();
        }
        return null;
    }

    /// <summary>
    /// Converts the response body (which may be an anonymous type) to a JsonElement.
    /// </summary>
    public static JsonElement ToJsonElement(BridgeResponse response)
    {
        if (response.Body is JsonElement element)
            return element;

        if (response.Body == null)
            return default;

        // Serialize and parse to get JsonElement
        var json = JsonSerializer.Serialize(response.Body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return JsonDocument.Parse(json).RootElement;
    }
}
