using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.Parsing;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private (SharpTSObject FunctionPrototype, SharpTSObject GeneratorPrototype)? _generatorIntrinsics;
    private (SharpTSObject FunctionPrototype, SharpTSObject GeneratorPrototype)? _asyncGeneratorIntrinsics;

    private SharpTSObject? _arrayIteratorPrototype;

    internal SharpTSObject GetArrayIteratorPrototype()
    {
        if (_arrayIteratorPrototype is not null) return _arrayIteratorPrototype;
        var prototype = new SharpTSObject([])
        {
            Prototype = GetIteratorPrototype(),
            PreserveCallableValueIdentity = true
        };
        var next = BuiltInMethod.CreateV2("next", 0, int.MaxValue, static (_, receiver, _) =>
            receiver.ToObject() is SharpTSArrayIterator iterator
                ? RuntimeValue.FromObject(iterator.Next())
                : throw new ThrowException(new SharpTSTypeError("Array iterator next called on an incompatible receiver")));
        prototype.DefineProperty("next", SharpTSPropertyDescriptor.ForMethod(next.AsNonConstructor().MarkRealmOwned()));
        prototype.DefineProperty(SharpTSSymbol.ToStringTag,
            new SharpTSPropertyDescriptor(value: "Array Iterator", configurable: true));
        return _arrayIteratorPrototype = prototype;
    }

    internal (SharpTSObject FunctionPrototype, SharpTSObject GeneratorPrototype) GetGeneratorIntrinsics(bool isAsync)
        => isAsync
            ? _asyncGeneratorIntrinsics ??= CreateGeneratorIntrinsics(isAsync: true)
            : _generatorIntrinsics ??= CreateGeneratorIntrinsics(isAsync: false);

    private SharpTSObject? _iteratorPrototype;
    private SharpTSObject GetIteratorPrototype() => _iteratorPrototype ??= CreateIteratorPrototype(false);

    private SharpTSObject CreateIteratorPrototype(bool isAsync)
    {
        var iteratorPrototype = new SharpTSObject([])
        { Prototype = GetObjectPrototype(), PreserveCallableValueIdentity = true };
        var iteratorSymbol = isAsync ? SharpTSSymbol.AsyncIterator : SharpTSSymbol.Iterator;
        iteratorPrototype.DefineProperty(iteratorSymbol, SharpTSPropertyDescriptor.ForMethod(
            BuiltInMethod.CreateV2(isAsync ? "[Symbol.asyncIterator]" : "[Symbol.iterator]", 0,
                static (_, receiver, _) => receiver).AsNonConstructor().MarkRealmOwned()));
        if (!isAsync)
        {
            foreach (var name in new[] { "map", "filter", "take", "drop", "flatMap", "reduce",
                "toArray", "forEach", "some", "every", "find" })
                iteratorPrototype.DefineProperty(name, SharpTSPropertyDescriptor.ForMethod(
                    ((BuiltInMethod)GeneratorBuiltIns.GetMember(null!, name)!).MarkRealmOwned()));
        }

        return iteratorPrototype;
    }

    private (SharpTSObject FunctionPrototype, SharpTSObject GeneratorPrototype) CreateGeneratorIntrinsics(bool isAsync)
    {
        var iteratorPrototype = isAsync ? CreateIteratorPrototype(true) : GetIteratorPrototype();
        var generatorPrototype = new SharpTSObject([])
        { Prototype = iteratorPrototype, PreserveCallableValueIdentity = true };
        foreach (var name in new[] { "next", "return", "throw" })
        {
            var method = isAsync
                ? AsyncGeneratorBuiltIns.GetMember(null!, name)
                : GeneratorBuiltIns.GetMember(null!, name);
            if (method is BuiltInMethod synchronous)
                synchronous.WithSpecLength(1).AsNonConstructor().MarkRealmOwned();
            if (method is BuiltInAsyncMethod asynchronous)
                asynchronous.WithSpecLength(1);
            generatorPrototype.DefineProperty(name, SharpTSPropertyDescriptor.ForMethod((ISharpTSCallable)method!));
        }
        generatorPrototype.DefineProperty(SharpTSSymbol.ToStringTag,
            new SharpTSPropertyDescriptor(value: isAsync ? "AsyncGenerator" : "Generator", configurable: true));

        var functionPrototype = new SharpTSObject([])
        { Prototype = GetFunctionPrototype(), PreserveCallableValueIdentity = true };
        functionPrototype.DefineProperty("prototype",
            new SharpTSPropertyDescriptor(value: generatorPrototype, configurable: true));
        functionPrototype.DefineProperty(SharpTSSymbol.ToStringTag,
            new SharpTSPropertyDescriptor(value: isAsync ? "AsyncGeneratorFunction" : "GeneratorFunction", configurable: true));
        generatorPrototype.DefineProperty("constructor",
            new SharpTSPropertyDescriptor(value: functionPrototype, configurable: true));

        // Reflective constructor identity is part of the intrinsic chain. Dynamic
        // generator-source construction remains outside this prototype support.
        var constructor = BuiltInMethod.CreateV2(isAsync ? "AsyncGeneratorFunction" : "GeneratorFunction",
            0, int.MaxValue, (_, _, args) =>
            {
                if (args.Length != 0)
                    throw new ThrowException(new SharpTSTypeError("Dynamic generator function source construction is not supported"));
                return RuntimeValue.FromObject(CreateEmptyGeneratorFunction(isAsync));
            })
            .WithSpecLength(1)
            .WithOwnProperties(new() { ["prototype"] = functionPrototype })
            .CloneForRealm();
        functionPrototype.DefineProperty("constructor",
            new SharpTSPropertyDescriptor(value: constructor, configurable: true));
        return (functionPrototype, generatorPrototype);
    }

    private ISharpTSGeneratorFunctionValue CreateEmptyGeneratorFunction(bool isAsync)
    {
        string source = isAsync ? "async function* anonymous() {}" : "function* anonymous() {}";
        var declaration = (Stmt.Function)new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()[0];
        var closure = new RuntimeEnvironment(strictMode: false);
        ISharpTSGeneratorFunctionValue function = isAsync
            ? new SharpTSAsyncGeneratorFunction(declaration, closure, creationRealm: this) { ComputedName = "anonymous" }
            : new SharpTSGeneratorFunction(declaration, closure, creationRealm: this) { ComputedName = "anonymous" };
        return function;
    }
}
