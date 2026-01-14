using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Defines the type signatures for built-in Node.js-compatible modules.
/// </summary>
public static class BuiltInModuleTypes
{
    private static TypeInfo BooleanType => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);

    /// <summary>
    /// Gets the exported types for the path module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetPathModuleTypes()
    {
        return new Dictionary<string, TypeInfo>
        {
            // Methods
            ["join"] = new TypeInfo.Function(
                [new TypeInfo.Any()],
                new TypeInfo.String(),
                HasRestParam: true
            ),
            ["resolve"] = new TypeInfo.Function(
                [new TypeInfo.Any()],
                new TypeInfo.String(),
                HasRestParam: true
            ),
            ["basename"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.String()],
                new TypeInfo.String(),
                RequiredParams: 1  // Second param is optional
            ),
            ["dirname"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["extname"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["normalize"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["isAbsolute"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                BooleanType
            ),
            ["relative"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["parse"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["root"] = new TypeInfo.String(),
                    ["dir"] = new TypeInfo.String(),
                    ["base"] = new TypeInfo.String(),
                    ["name"] = new TypeInfo.String(),
                    ["ext"] = new TypeInfo.String()
                }.ToFrozenDictionary())
            ),
            ["format"] = new TypeInfo.Function(
                [new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["root"] = new TypeInfo.String(),
                    ["dir"] = new TypeInfo.String(),
                    ["base"] = new TypeInfo.String(),
                    ["name"] = new TypeInfo.String(),
                    ["ext"] = new TypeInfo.String()
                }.ToFrozenDictionary())],
                new TypeInfo.String()
            ),
            // Properties
            ["sep"] = new TypeInfo.String(),
            ["delimiter"] = new TypeInfo.String()
        };
    }

    /// <summary>
    /// Gets the exported types for the os module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetOsModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);

        return new Dictionary<string, TypeInfo>
        {
            // Methods returning strings
            ["platform"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["arch"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["hostname"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["homedir"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["tmpdir"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["type"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["release"] = new TypeInfo.Function([], new TypeInfo.String()),

            // Methods returning numbers
            ["totalmem"] = new TypeInfo.Function([], numberType),
            ["freemem"] = new TypeInfo.Function([], numberType),

            // Methods returning arrays/objects
            ["cpus"] = new TypeInfo.Function([],
                new TypeInfo.Array(new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["model"] = new TypeInfo.String(),
                    ["speed"] = numberType
                }.ToFrozenDictionary()))
            ),
            ["userInfo"] = new TypeInfo.Function([],
                new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["username"] = new TypeInfo.String(),
                    ["uid"] = numberType,
                    ["gid"] = numberType,
                    ["shell"] = new TypeInfo.Union([new TypeInfo.String(), new TypeInfo.Null()]),
                    ["homedir"] = new TypeInfo.String()
                }.ToFrozenDictionary())
            ),

            // Properties
            ["EOL"] = new TypeInfo.String()
        };
    }

    /// <summary>
    /// Gets the exported types for the fs module (sync APIs only).
    /// </summary>
    public static Dictionary<string, TypeInfo> GetFsModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);

        // Stats-like return type for statSync/lstatSync
        var statsType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["isDirectory"] = BooleanType,
            ["isFile"] = BooleanType,
            ["size"] = numberType
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            // File check - returns false on error (doesn't throw)
            ["existsSync"] = new TypeInfo.Function([new TypeInfo.String()], BooleanType),

            // Read file - returns string if encoding provided, Buffer (array) otherwise
            ["readFileSync"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.Union([new TypeInfo.String(), new TypeInfo.Null()])],
                new TypeInfo.Union([new TypeInfo.String(), new TypeInfo.Array(numberType)]),
                RequiredParams: 1
            ),

            // Write operations - return void
            ["writeFileSync"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.Union([new TypeInfo.String(), new TypeInfo.Array(numberType)])],
                new TypeInfo.Void()
            ),
            ["appendFileSync"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.String()],
                new TypeInfo.Void()
            ),

            // File/directory deletion
            ["unlinkSync"] = new TypeInfo.Function([new TypeInfo.String()], new TypeInfo.Void()),
            ["rmdirSync"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.Any()],
                new TypeInfo.Void(),
                RequiredParams: 1
            ),

            // Directory operations
            ["mkdirSync"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.Any()],
                new TypeInfo.Void(),
                RequiredParams: 1
            ),
            ["readdirSync"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.Array(new TypeInfo.String())
            ),

            // File info
            ["statSync"] = new TypeInfo.Function([new TypeInfo.String()], statsType),
            ["lstatSync"] = new TypeInfo.Function([new TypeInfo.String()], statsType),

            // File move/copy
            ["renameSync"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.String()],
                new TypeInfo.Void()
            ),
            ["copyFileSync"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.String()],
                new TypeInfo.Void()
            ),

            // Access check - throws if not accessible
            ["accessSync"] = new TypeInfo.Function(
                [new TypeInfo.String(), numberType],
                new TypeInfo.Void(),
                RequiredParams: 1
            )
        };
    }

    /// <summary>
    /// Gets the exported types for a built-in module by name.
    /// </summary>
    /// <param name="moduleName">The module name (e.g., "path", "fs", "os").</param>
    /// <returns>The exported types, or null if not a known built-in module.</returns>
    public static Dictionary<string, TypeInfo>? GetModuleTypes(string moduleName)
    {
        return moduleName switch
        {
            "path" => GetPathModuleTypes(),
            "os" => GetOsModuleTypes(),
            "fs" => GetFsModuleTypes(),
            _ => null
        };
    }
}
