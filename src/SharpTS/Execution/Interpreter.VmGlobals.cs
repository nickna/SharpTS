using SharpTS.Parsing;

namespace SharpTS.Execution;

public partial class Interpreter
{
    internal void SeedVmGlobalObjectProperties(Dictionary<string, object?> properties)
    {
        foreach (var (name, value) in properties)
            GlobalThis.SetProperty(name,
                Runtime.BuiltIns.Modules.Interpreter.VmExecutionHost.Current is { } host ? host.ToVm(value) : value);
    }

    // VM scripts own classic global var bindings. Direct eval and the REPL
    // keep their existing scope setup and do not call this entry point.
    internal void PrepareVmGlobalVarBindings(List<Stmt> statements)
    {
        HoistFunctionDeclarations(statements);
        HoistTopLevelVarDeclarations(statements);
        foreach (var name in InterpreterVarDeclarations.CollectLoopHeads(statements))
            RegisterClassicGlobalVar(name.Lexeme);
        GlobalThis.BeginVmWriteTracking();
    }
}
