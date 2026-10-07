using System.Reflection;
using System.Xml.Linq;
using SharpTS.IO;
using SharpTS.Compilation;

namespace SharpTS.LanguageServer.Documentation;

/// <summary>
/// Loads and caches XML documentation from .xml files alongside assemblies.
/// </summary>
public sealed class XmlDocLoader
{
    private const int CacheLimit = 64;
    private readonly object _gate = new();
    private readonly Dictionary<string, CachedDocument> _loadedDocs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the summary documentation for a type.
    /// </summary>
    public string? GetTypeSummary(Type type)
    {
        var doc = GetDocument(type.Assembly);
        if (doc == null) return null;

        var memberKey = $"T:{type.FullName}";
        return GetMemberSummary(doc, memberKey);
    }

    /// <summary>
    /// Gets the summary documentation for a method.
    /// </summary>
    public string? GetMethodSummary(Type type, string methodName)
    {
        var doc = GetDocument(type.Assembly);
        if (doc == null) return null;

        var memberKey = $"M:{type.FullName}.{methodName}";
        return GetMemberSummary(doc, memberKey);
    }

    /// <summary>
    /// Gets the summary documentation for a property.
    /// </summary>
    public string? GetPropertySummary(Type type, string propertyName)
    {
        var doc = GetDocument(type.Assembly);
        if (doc == null) return null;

        var memberKey = $"P:{type.FullName}.{propertyName}";
        return GetMemberSummary(doc, memberKey);
    }

    /// <summary>
    /// Gets the summary documentation for a constructor parameter.
    /// </summary>
    public string? GetParameterSummary(Type type, string parameterName)
    {
        var doc = GetDocument(type.Assembly);
        if (doc == null) return null;

        // Search constructors for parameter documentation
        var members = doc.Root?.Element("members");
        if (members == null) return null;

        foreach (var member in members.Elements("member"))
        {
            var name = member.Attribute("name")?.Value;
            if (name == null || !name.StartsWith($"M:{type.FullName}.#ctor"))
                continue;

            var param = member.Elements("param")
                .FirstOrDefault(p => p.Attribute("name")?.Value == parameterName);

            if (param != null)
            {
                return CleanupDocumentation(param.Value);
            }
        }

        return null;
    }

    private string? GetMemberSummary(XDocument doc, string memberKey)
    {
        var members = doc.Root?.Element("members");
        if (members == null) return null;

        var member = members.Elements("member")
            .FirstOrDefault(m => m.Attribute("name")?.Value == memberKey);

        if (member == null) return null;

        var summary = member.Element("summary")?.Value;
        return summary != null ? CleanupDocumentation(summary) : null;
    }

    private XDocument? GetDocument(Assembly assembly)
    {
        var location = AssemblyReferenceLoader.GetSourcePath(assembly);
        if (string.IsNullOrEmpty(location))
            return null;

        var xmlPath = Path.ChangeExtension(location, ".xml");
        string? text;
        try
        {
            text = CompilerFileSystem.FileExists(xmlPath) ? CompilerFileSystem.ReadAllText(xmlPath) : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        lock (_gate)
        {
            if (_loadedDocs.TryGetValue(location, out CachedDocument? cached) &&
                string.Equals(cached.Text, text, StringComparison.Ordinal))
            {
                return cached.Document;
            }

            XDocument? document = null;
            if (text is not null)
            {
                try { document = XDocument.Parse(text); }
                catch (System.Xml.XmlException) { }
            }
            if (!_loadedDocs.ContainsKey(location) && _loadedDocs.Count >= CacheLimit)
                _loadedDocs.Remove(_loadedDocs.Keys.First());
            _loadedDocs[location] = new CachedDocument(text, document);
            return document;
        }
    }

    private sealed record CachedDocument(string? Text, XDocument? Document);

    private static string CleanupDocumentation(string text)
    {
        // Remove leading/trailing whitespace and normalize internal whitespace
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l));

        return string.Join(" ", lines);
    }
}
