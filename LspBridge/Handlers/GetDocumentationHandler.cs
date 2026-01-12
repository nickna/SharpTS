using SharpTS.Compilation;
using SharpTS.LspBridge.Documentation;
using SharpTS.LspBridge.Protocol;

namespace SharpTS.LspBridge.Handlers;

/// <summary>
/// Handles the get-type-documentation command to return XML documentation.
/// </summary>
public sealed class GetDocumentationHandler : ICommandHandler
{
    private readonly AssemblyReferenceLoader _loader;
    private readonly XmlDocLoader _docLoader;

    public GetDocumentationHandler(AssemblyReferenceLoader loader)
    {
        _loader = loader;
        _docLoader = new XmlDocLoader();
    }

    public BridgeResponse Handle(BridgeRequest request)
    {
        var typeName = request.GetStringArgument("typeName");
        if (string.IsNullOrEmpty(typeName))
        {
            return BridgeResponse.Error("typeName argument is required");
        }

        // Try to resolve the type
        var type = _loader.TryResolve(typeName);
        if (type == null && !typeName.EndsWith("Attribute", StringComparison.Ordinal))
        {
            type = _loader.TryResolve(typeName + "Attribute");
        }

        if (type == null)
        {
            return BridgeResponse.Error($"Type not found: {typeName}");
        }

        var documentation = _docLoader.GetTypeSummary(type);

        return BridgeResponse.Ok(new
        {
            fullName = type.FullName,
            documentation
        });
    }
}
