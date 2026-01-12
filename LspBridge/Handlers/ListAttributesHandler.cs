using SharpTS.Compilation;
using SharpTS.LspBridge.Protocol;

namespace SharpTS.LspBridge.Handlers;

/// <summary>
/// Handles the list-attributes command to return all available attribute types.
/// </summary>
public sealed class ListAttributesHandler : ICommandHandler
{
    private readonly AssemblyReferenceLoader _loader;
    private List<AttributeInfo>? _cachedAttributes;

    public ListAttributesHandler(AssemblyReferenceLoader loader)
    {
        _loader = loader;
    }

    public BridgeResponse Handle(BridgeRequest request)
    {
        // Cache attributes on first call (they don't change during session)
        _cachedAttributes ??= CollectAllAttributes();

        var filter = request.GetStringArgument("filter");
        var results = string.IsNullOrEmpty(filter)
            ? _cachedAttributes
            : _cachedAttributes.Where(a =>
                a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                a.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
              .ToList();

        return BridgeResponse.Ok(new { attributes = results });
    }

    private List<AttributeInfo> CollectAllAttributes()
    {
        var attributes = new List<AttributeInfo>();

        foreach (var type in _loader.GetAllPublicTypes())
        {
            if (!IsAttribute(type)) continue;
            if (type.IsAbstract) continue;

            var name = type.Name;
            // Remove "Attribute" suffix for cleaner display
            if (name.EndsWith("Attribute", StringComparison.Ordinal))
                name = name[..^9];

            attributes.Add(new AttributeInfo
            {
                Name = name,
                FullName = type.FullName ?? type.Name,
                Namespace = type.Namespace ?? "",
                Assembly = type.Assembly.GetName().Name ?? ""
            });
        }

        return attributes
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private sealed class AttributeInfo
    {
        public string Name { get; init; } = "";
        public string FullName { get; init; } = "";
        public string Namespace { get; init; } = "";
        public string Assembly { get; init; } = "";
    }
}
