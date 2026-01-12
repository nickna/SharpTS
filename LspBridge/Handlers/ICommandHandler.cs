using SharpTS.LspBridge.Protocol;

namespace SharpTS.LspBridge.Handlers;

/// <summary>
/// Interface for LSP bridge command handlers.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Handles a bridge request and returns a response.
    /// </summary>
    BridgeResponse Handle(BridgeRequest request);
}
