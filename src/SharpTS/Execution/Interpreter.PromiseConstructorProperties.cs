using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    // The internal superclass view and the public intrinsic are two views of
    // the same Promise constructor. Never recognize a guest name as that owner.
    internal bool TryGetPromiseConstructorSymbolProperties(object? owner, out SharpTSObject properties)
    {
        var realm = GetPromiseConstructorState(owner);
        bool isInternal = owner is SharpTSPromiseClass && ReferenceEquals(owner, SharpTSPromiseClass.PromiseBase);
        if (realm is null && !isInternal)
        {
            properties = null!;
            return false;
        }

        properties = (realm ?? PromiseRealmState).SymbolProperties;
        return true;
    }

    internal static SharpTSObject CreatePromiseConstructorSymbolProperties(SharpTSFunctionPrototype functionPrototype)
    {
        var properties = new SharpTSObject([]);
        var getter = new PromiseSpeciesGetter();
        FunctionObjectProperties.SetExplicitPrototype(getter.OwnPropertyStorage, functionPrototype);
        properties.DefineProperty(SharpTSSymbol.Species, new SharpTSPropertyDescriptor
        {
            Get = getter,
            RawGet = getter,
            HasGet = true,
            HasSet = true,
            RawSet = SharpTSUndefined.Instance,
            Enumerable = false,
            HasEnumerable = true,
            Configurable = true,
            HasConfigurable = true,
        });
        return properties;
    }

    private sealed class PromiseSpeciesGetter : BuiltInMethod, IUndefinedThisDefault
    {
        internal PromiseSpeciesGetter()
            : base("get [Symbol.species]", 0, int.MaxValue, null,
                static (_, receiver, _) => receiver, SharpTSUndefined.Instance)
        {
            AsNonConstructor();
            MarkRealmOwned();
        }
    }
}
