using SharpTS.Compilation;
using SharpTS.LspBridge.Protocol;

namespace SharpTS.LspBridge.Handlers;

/// <summary>
/// Handles the resolve-type command to check if a type exists and whether it's an attribute.
/// </summary>
public sealed class ResolveTypeHandler(AssemblyReferenceLoader loader) : ICommandHandler
{
    public BridgeResponse Handle(BridgeRequest request)
    {
        var typeName = request.GetStringArgument("typeName");
        if (string.IsNullOrEmpty(typeName))
        {
            return BridgeResponse.Error("typeName argument is required");
        }

        // Try to resolve the type directly
        var type = loader.TryResolve(typeName);

        // If not found, try with "Attribute" suffix (common pattern)
        if (type == null && !typeName.EndsWith("Attribute", StringComparison.Ordinal))
        {
            type = loader.TryResolve(typeName + "Attribute");
        }

        if (type == null)
        {
            return BridgeResponse.Ok(new
            {
                exists = false
            });
        }

        return BridgeResponse.Ok(new
        {
            exists = true,
            fullName = type.FullName,
            isAttribute = IsAttribute(type),
            isAbstract = type.IsAbstract,
            isSealed = type.IsSealed,
            assembly = type.Assembly.GetName().Name
        });
    }

    private static bool IsAttribute(Type type)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.FullName == "System.Attribute")
                return true;
            current = current.BaseType;
        }
        return false;
    }
}
