namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the <c>primitive:tty</c> primitive module.
/// Exposes a single <c>isatty(fd)</c> method that returns whether the given
/// file descriptor is a TTY. The user-facing <c>tty</c> module lives in
/// <c>stdlib/node/tty.ts</c> and re-exports this primitive directly.
/// </summary>
public static class TtyPrimitiveInterpreter
{
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["isatty"] = BuiltInMethod.CreateV2("isatty", 1, (interp, recv, args) =>
            {
                var fd = Convert.ToInt32(args[0].ToObject());
                return RuntimeValue.FromBoolean(fd switch
                {
                    0 => !Console.IsInputRedirected,
                    1 => !Console.IsOutputRedirected,
                    2 => !Console.IsErrorRedirected,
                    _ => false
                });
            })
        };
    }
}
