using System.Text.Json.Serialization;

namespace SharpTS.Packaging;

/// <summary>
/// Model for npm package.json file structure.
/// Only includes fields relevant for NuGet package generation.
/// </summary>
public class PackageJson
{
    /// <summary>
    /// Package name (maps to NuGet Package ID).
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Package version (SemVer format).
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// Package description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Author name or "Name &lt;email&gt;" format.
    /// </summary>
    [JsonPropertyName("author")]
    public object? AuthorRaw { get; set; }

    /// <summary>
    /// Gets the author as a string, handling both string and object formats.
    /// </summary>
    [JsonIgnore]
    public string? Author
    {
        get
        {
            if (AuthorRaw is string s)
                return s;
            if (AuthorRaw is System.Text.Json.JsonElement element)
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                    return element.GetString();
                if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var name = element.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var email = element.TryGetProperty("email", out var e) ? e.GetString() : null;
                    if (name != null && email != null)
                        return $"{name} <{email}>";
                    return name;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// SPDX license identifier (e.g., "MIT", "Apache-2.0").
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// Repository information.
    /// </summary>
    [JsonPropertyName("repository")]
    public object? RepositoryRaw { get; set; }

    /// <summary>
    /// Gets the repository URL, handling both string and object formats.
    /// </summary>
    [JsonIgnore]
    public string? RepositoryUrl
    {
        get
        {
            if (RepositoryRaw is string s)
                return s;
            if (RepositoryRaw is System.Text.Json.JsonElement element)
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                    return element.GetString();
                if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (element.TryGetProperty("url", out var url))
                        return url.GetString();
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Package keywords/tags.
    /// </summary>
    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }

    /// <summary>
    /// Homepage URL.
    /// </summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    /// <summary>
    /// Gets the project URL (homepage or repository URL).
    /// </summary>
    [JsonIgnore]
    public string? ProjectUrl => Homepage ?? RepositoryUrl;

    /// <summary>
    /// Validates that required fields are present.
    /// </summary>
    public bool IsValid(out List<string> errors)
    {
        errors = [];

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("package.json 'name' field is required");

        if (string.IsNullOrWhiteSpace(Version))
            errors.Add("package.json 'version' field is required");

        return errors.Count == 0;
    }
}
