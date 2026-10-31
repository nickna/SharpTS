using System.Runtime.CompilerServices;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

// Realm-owned intrinsic data only. VM evaluators may be disposed and recreated
// without replacing this constructor, its ordinary properties or its prototypes.
internal sealed class PromiseIntrinsicState
{
    private SharpTSObject? _symbolProperties;
    private SharpTSPromisePrototype? _prototype;
    private readonly ConditionalWeakTable<BuiltInMethod, BuiltInMethod> _methods = new();

    internal PromiseIntrinsicState(SharpTSFunctionPrototype functionPrototype, SharpTSObjectPrototype objectPrototype,
        SharpTSObjectNamespace objectConstructor)
    {
        Constructor = Interpreter.PromiseConstructorSentinel.CloneForRealm(this);
        FunctionPrototype = functionPrototype;
        ObjectPrototype = objectPrototype;
        ObjectConstructor = objectConstructor;
        functionPrototype.RealmObjectPrototype ??= objectPrototype;
    }

    internal SharpTSBuiltInConstructor Constructor { get; }
    internal SharpTSFunctionPrototype FunctionPrototype { get; }
    internal SharpTSObjectPrototype ObjectPrototype { get; }
    internal SharpTSObjectNamespace ObjectConstructor { get; }
    internal SharpTSPromisePrototype Prototype => _prototype ??= new() { RealmConstructor = Constructor };
    internal SharpTSObject SymbolProperties
        => _symbolProperties ??= Interpreter.CreatePromiseConstructorSymbolProperties(FunctionPrototype);

    internal Dictionary<SharpTSBuiltInConstructor, Dictionary<string, SharpTSPropertyDescriptor>> StringProperties { get; } = [];
    internal Dictionary<SharpTSBuiltInConstructor, HashSet<string>> DeletedStringProperties { get; } = [];

    internal BuiltInMethod LocalizeMethod(BuiltInMethod method)
        => method.IsRealmOwned ? method : _methods.GetValue(method, template =>
        {
            var copy = template.CloneForRealm();
            FunctionObjectProperties.SetExplicitPrototype(copy.OwnPropertyStorage, FunctionPrototype);
            return copy;
        });
}
