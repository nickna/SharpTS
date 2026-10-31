using SharpTS.Parsing;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private Runtime.VmGlobalLexicalState? _vmLexicalState;
    internal IReadOnlyDictionary<string, object?> CaptureVmGlobalIntrinsics()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in GlobalNames.Distinct(StringComparer.Ordinal))
            if (ResolveGlobalObjectIntrinsic(name) is { } value)
            {
                // These actual namespace producers otherwise keep a bound
                // evaluator method as a lazy prototype factory. A persistent
                // context retains the already-created prototype data instead.
                switch (value)
                {
                    case Runtime.Types.SharpTSStringNamespace text:
                        text.RealmPrototypeFactory = ConstantVmPrototype(GetStringPrototype());
                        break;
                    case Runtime.Types.SharpTSNumberNamespace number:
                        number.RealmPrototypeFactory = ConstantVmPrototype(GetNumberPrototype());
                        break;
                    case Runtime.Types.SharpTSBooleanNamespace boolean:
                        boolean.RealmPrototypeFactory = ConstantVmPrototype(GetBooleanPrototype());
                        break;
                }
                values[name] = value;
            }
        return values;
    }

    private static Func<T> ConstantVmPrototype<T>(T prototype) => () => prototype;

    internal void UseVmGlobalObject(Runtime.Types.SharpTSGlobalThis globalObject) => _globalThis = globalObject;

    internal void SeedVmGlobalObjectProperties(Dictionary<string, object?> properties, object? context = null)
    {
        _vmLexicalState = Runtime.Types.VmContext.GetLexicalState(context);
        bool shared = Runtime.Types.VmContext.ApplyGlobalObjectState(context, this);
        _environment.VmGlobalObject = GlobalThis;
        if (shared) return;
        foreach (var (name, value) in properties)
            GlobalThis.SetProperty(name,
                Runtime.BuiltIns.Modules.Interpreter.VmExecutionHost.Current is { } host ? host.ToVm(value) : value);
    }

    // VM scripts own classic global var bindings. Direct eval and the REPL
    // keep their existing scope setup and do not call this entry point.
    internal void PrepareVmGlobalVarBindings(List<Stmt> statements)
    {
        if (DirectivePrologue.HasUseStrict(statements))
            _environment = new Runtime.RuntimeEnvironment(_environment, strictMode: true);
        if (_vmLexicalState is { } lexicals)
        {
            lexicals.Prepare(statements, GlobalThis.HasRestrictedVmLexicalProperty);
            _environment.VmLexicalBindings = lexicals;
        }
        HoistFunctionDeclarations(statements);
        var functions = Runtime.VmGlobalLexicalState.TopLevelFunctions(statements)
            .Distinct(StringComparer.Ordinal)
            .Select(name => (Name: name, Value: _environment.GetAt(0, name))).ToArray();
        HoistTopLevelVarDeclarations(statements);
        foreach (var name in InterpreterVarDeclarations.CollectLoopHeads(statements))
            RegisterClassicGlobalVar(name.Lexeme);
        ReserveLexicalDeclarations(statements);
        GlobalThis.BeginVmWriteTracking();
        // Function declarations create initialized object bindings as well as
        // private non-configurable records used by later declaration checks.
        foreach (var (name, value) in functions)
        {
            RegisterClassicGlobalVar(name);
            WriteClassicGlobalVariable(name, value, _environment.IsStrictMode);
        }
    }

    internal void PublishVmLexicalValues()
    {
        if (_vmLexicalState is not { } lexicals) return;
        foreach (var value in lexicals.InitializedValues().ToArray())
            Runtime.BuiltIns.Modules.Interpreter.VmModuleInterpreter.RecordVmBoundaryValue(value.ToObject(), this);
    }
}
