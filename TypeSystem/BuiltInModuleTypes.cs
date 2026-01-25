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
    /// Gets the exported types for the querystring module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetQuerystringModuleTypes()
    {
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();

        return new Dictionary<string, TypeInfo>
        {
            // parse(str, sep?, eq?, options?) -> object
            ["parse"] = new TypeInfo.Function(
                [stringType, stringType, stringType, anyType],
                anyType,
                RequiredParams: 1
            ),
            // stringify(obj, sep?, eq?, options?) -> string
            ["stringify"] = new TypeInfo.Function(
                [anyType, stringType, stringType, anyType],
                stringType,
                RequiredParams: 1
            ),
            // escape(str) -> string
            ["escape"] = new TypeInfo.Function([stringType], stringType),
            // unescape(str) -> string
            ["unescape"] = new TypeInfo.Function([stringType], stringType),
            // decode is alias for parse
            ["decode"] = new TypeInfo.Function(
                [stringType, stringType, stringType, anyType],
                anyType,
                RequiredParams: 1
            ),
            // encode is alias for stringify
            ["encode"] = new TypeInfo.Function(
                [anyType, stringType, stringType, anyType],
                stringType,
                RequiredParams: 1
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the assert module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetAssertModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var stringType = new TypeInfo.String();
        var voidType = new TypeInfo.Void();

        return new Dictionary<string, TypeInfo>
        {
            // ok(value, message?) -> void
            ["ok"] = new TypeInfo.Function(
                [anyType, stringType],
                voidType,
                RequiredParams: 1
            ),
            // strictEqual(actual, expected, message?) -> void
            ["strictEqual"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            ),
            // notStrictEqual(actual, expected, message?) -> void
            ["notStrictEqual"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            ),
            // deepStrictEqual(actual, expected, message?) -> void
            ["deepStrictEqual"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            ),
            // notDeepStrictEqual(actual, expected, message?) -> void
            ["notDeepStrictEqual"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            ),
            // throws(fn, message?) -> void
            ["throws"] = new TypeInfo.Function(
                [anyType, stringType],
                voidType,
                RequiredParams: 1
            ),
            // doesNotThrow(fn, message?) -> void
            ["doesNotThrow"] = new TypeInfo.Function(
                [anyType, stringType],
                voidType,
                RequiredParams: 1
            ),
            // fail(message?) -> void
            ["fail"] = new TypeInfo.Function(
                [stringType],
                voidType,
                RequiredParams: 0
            ),
            // equal(actual, expected, message?) -> void (loose equality)
            ["equal"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            ),
            // notEqual(actual, expected, message?) -> void (loose equality)
            ["notEqual"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the url module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetUrlModuleTypes()
    {
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();

        // URL class type (simplified - represents the URL constructor/class)
        var urlClassType = new TypeInfo.Any(); // Full class typing would require more infrastructure

        // URLSearchParams class type
        var urlSearchParamsType = new TypeInfo.Any();

        return new Dictionary<string, TypeInfo>
        {
            // URL class constructor
            ["URL"] = urlClassType,
            // URLSearchParams class constructor
            ["URLSearchParams"] = urlSearchParamsType,
            // parse function (legacy)
            ["parse"] = new TypeInfo.Function(
                [stringType, stringType, anyType],
                anyType,
                RequiredParams: 1
            ),
            // format function (legacy)
            ["format"] = new TypeInfo.Function(
                [anyType],
                stringType
            ),
            // resolve function (legacy)
            ["resolve"] = new TypeInfo.Function(
                [stringType, stringType],
                stringType
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the process module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetProcessModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var voidType = new TypeInfo.Void();
        var anyType = new TypeInfo.Any();

        return new Dictionary<string, TypeInfo>
        {
            // Properties
            ["platform"] = stringType,
            ["arch"] = stringType,
            ["pid"] = numberType,
            ["version"] = stringType,
            ["env"] = new TypeInfo.Record(new Dictionary<string, TypeInfo>().ToFrozenDictionary()), // Record<string, string>
            ["argv"] = new TypeInfo.Array(stringType),
            ["exitCode"] = numberType,
            ["stdin"] = anyType,
            ["stdout"] = anyType,
            ["stderr"] = anyType,

            // Methods
            ["cwd"] = new TypeInfo.Function([], stringType),
            ["chdir"] = new TypeInfo.Function([stringType], voidType),
            ["exit"] = new TypeInfo.Function([numberType], voidType, RequiredParams: 0),
            ["hrtime"] = new TypeInfo.Function(
                [new TypeInfo.Array(numberType)],
                new TypeInfo.Array(numberType),
                RequiredParams: 0
            ),
            ["uptime"] = new TypeInfo.Function([], numberType),
            ["memoryUsage"] = new TypeInfo.Function([],
                new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["rss"] = numberType,
                    ["heapTotal"] = numberType,
                    ["heapUsed"] = numberType,
                    ["external"] = numberType,
                    ["arrayBuffers"] = numberType
                }.ToFrozenDictionary())
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the crypto module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetCryptoModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();

        return new Dictionary<string, TypeInfo>
        {
            // Methods
            ["createHash"] = new TypeInfo.Function([stringType], anyType), // Returns Hash object
            ["createHmac"] = new TypeInfo.Function([stringType, anyType], anyType), // Returns Hmac object
            ["randomBytes"] = new TypeInfo.Function([numberType], new TypeInfo.Array(numberType)),
            ["randomUUID"] = new TypeInfo.Function([], stringType),
            ["randomInt"] = new TypeInfo.Function([numberType, numberType], numberType, RequiredParams: 1)
        };
    }

    /// <summary>
    /// Gets the exported types for the util module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetUtilModuleTypes()
    {
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();

        return new Dictionary<string, TypeInfo>
        {
            // Methods
            ["format"] = new TypeInfo.Function([anyType], stringType, HasRestParam: true),
            ["inspect"] = new TypeInfo.Function([anyType, anyType], stringType, RequiredParams: 1),

            // util.types namespace
            ["types"] = new TypeInfo.Record(new Dictionary<string, TypeInfo>
            {
                ["isArray"] = new TypeInfo.Function([anyType], new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN)),
                ["isDate"] = new TypeInfo.Function([anyType], new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN)),
                ["isFunction"] = new TypeInfo.Function([anyType], new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN)),
                ["isNull"] = new TypeInfo.Function([anyType], new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN)),
                ["isUndefined"] = new TypeInfo.Function([anyType], new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN))
            }.ToFrozenDictionary())
        };
    }

    /// <summary>
    /// Gets the exported types for the readline module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetReadlineModuleTypes()
    {
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();
        var voidType = new TypeInfo.Void();

        return new Dictionary<string, TypeInfo>
        {
            // Methods
            ["questionSync"] = new TypeInfo.Function([stringType], stringType),
            ["createInterface"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0)
        };
    }

    /// <summary>
    /// Gets the exported types for the child_process module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetChildProcessModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();

        var spawnResultType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["stdout"] = stringType,
            ["stderr"] = stringType,
            ["status"] = numberType,
            ["signal"] = new TypeInfo.Union([stringType, new TypeInfo.Null()])
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            // Methods
            ["execSync"] = new TypeInfo.Function([stringType, anyType], stringType, RequiredParams: 1),
            ["spawnSync"] = new TypeInfo.Function(
                [stringType, new TypeInfo.Array(stringType), anyType],
                spawnResultType,
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
            "querystring" => GetQuerystringModuleTypes(),
            "assert" => GetAssertModuleTypes(),
            "url" => GetUrlModuleTypes(),
            "process" => GetProcessModuleTypes(),
            "crypto" => GetCryptoModuleTypes(),
            "util" => GetUtilModuleTypes(),
            "readline" => GetReadlineModuleTypes(),
            "child_process" => GetChildProcessModuleTypes(),
            _ => null
        };
    }
}
