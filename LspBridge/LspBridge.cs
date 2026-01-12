using System.Text.Json;
using System.Text.Json.Serialization;
using SharpTS.Compilation;
using SharpTS.LspBridge.Handlers;
using SharpTS.LspBridge.Protocol;

namespace SharpTS.LspBridge;

/// <summary>
/// LSP Bridge for IDE integration. Communicates via line-delimited JSON over stdin/stdout.
/// </summary>
public sealed class LspBridge : IDisposable
{
    private readonly AssemblyReferenceLoader _loader;
    private readonly Dictionary<string, ICommandHandler> _handlers;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;
    private bool _running;

    public LspBridge(IEnumerable<string> assemblyPaths, string? sdkPath = null)
    {
        _loader = new AssemblyReferenceLoader(assemblyPaths, sdkPath);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _handlers = new Dictionary<string, ICommandHandler>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolve-type"] = new ResolveTypeHandler(_loader),
            ["list-attributes"] = new ListAttributesHandler(_loader),
            ["get-attribute-info"] = new GetAttributeInfoHandler(_loader),
            ["get-type-documentation"] = new GetDocumentationHandler(_loader),
            ["shutdown"] = new ShutdownHandler(this)
        };
    }

    /// <summary>
    /// Runs the bridge message loop. Blocks until shutdown or EOF.
    /// </summary>
    public void Run()
    {
        _running = true;

        // Write ready signal
        WriteResponse(new BridgeResponse
        {
            Seq = 0,
            Success = true,
            Body = new { ready = true }
        });

        while (_running)
        {
            try
            {
                string? line = Console.ReadLine();
                if (line == null)
                {
                    // EOF - parent process closed stdin
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                ProcessMessage(line);
            }
            catch (Exception ex)
            {
                // Log to stderr but keep running
                Console.Error.WriteLine($"[LspBridge Error] {ex.Message}");
            }
        }
    }

    private void ProcessMessage(string json)
    {
        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(json, _jsonOptions);
            if (request == null)
            {
                WriteError(0, "Invalid request: null");
                return;
            }
        }
        catch (JsonException ex)
        {
            WriteError(0, $"JSON parse error: {ex.Message}");
            return;
        }

        if (string.IsNullOrEmpty(request.Command))
        {
            WriteError(request.Seq, "Missing command");
            return;
        }

        if (!_handlers.TryGetValue(request.Command, out var handler))
        {
            WriteError(request.Seq, $"Unknown command: {request.Command}");
            return;
        }

        try
        {
            var response = handler.Handle(request);
            response.Seq = request.Seq;
            WriteResponse(response);
        }
        catch (Exception ex)
        {
            WriteError(request.Seq, $"Command failed: {ex.Message}");
        }
    }

    private void WriteResponse(BridgeResponse response)
    {
        string json = JsonSerializer.Serialize(response, _jsonOptions);
        Console.WriteLine(json);
        Console.Out.Flush();
    }

    private void WriteError(int seq, string message)
    {
        WriteResponse(new BridgeResponse
        {
            Seq = seq,
            Success = false,
            Message = message
        });
    }

    /// <summary>
    /// Stops the message loop.
    /// </summary>
    public void Stop() => _running = false;

    public void Dispose()
    {
        if (!_disposed)
        {
            _loader.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Handler for the shutdown command.
/// </summary>
internal sealed class ShutdownHandler(LspBridge bridge) : ICommandHandler
{
    public BridgeResponse Handle(BridgeRequest request)
    {
        bridge.Stop();
        return BridgeResponse.Ok(new { shutdown = true });
    }
}
