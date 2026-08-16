using System.Text.Json;
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
    [JsonConverter(typeof(PackageAuthorConverter))]
    public string? Author { get; set; }

    /// <summary>
    /// SPDX license identifier (e.g., "MIT", "Apache-2.0").
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// Repository URL.
    /// </summary>
    [JsonPropertyName("repository")]
    [JsonConverter(typeof(PackageRepositoryConverter))]
    public string? RepositoryUrl { get; set; }

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

/// <summary>
/// Handles "author" field which can be a string or an object.
/// </summary>
public class PackageAuthorConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
            
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(email))
                return $"{name} <{email}>";
                
            return name;
        }
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}

/// <summary>
/// Handles "repository" field which can be a string or an object.
/// </summary>
public class PackageRepositoryConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("url", out var url))
            {
                return url.GetString();
            }
        }
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
