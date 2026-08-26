using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter backing for the host-sensitive part of <c>node:module</c>.
/// The public catalog and lookup helpers remain in the TypeScript facade.
/// </summary>
public static class ModulePrimitiveInterpreter
{
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["createRequire"] = BuiltInMethod.CreateV2("createRequire", 1, CreateRequire)
        };
    }

    private static RuntimeValue CreateRequire(
        Execution.Interpreter interpreter,
        RuntimeValue receiver,
        ReadOnlySpan<RuntimeValue> args)
    {
        var callerPath = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "" : "";
        if (Uri.TryCreate(callerPath, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            callerPath = uri.LocalPath;
        }
        else if (!Path.IsPathRooted(callerPath))
        {
            callerPath = Path.GetFullPath(callerPath);
        }

        var require = BuiltInMethod.CreateV2("require", 1, (callInterpreter, _, requireArgs) =>
        {
            var specifier = requireArgs.Length > 0
                ? requireArgs[0].ToObject()?.ToString() ?? ""
                : "";
            return RuntimeValue.FromBoxed(
                callInterpreter.RequireCommonJsModule(specifier, callerPath));
        });

        return RuntimeValue.FromObject(require);
    }
}
