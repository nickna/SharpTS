using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private SharpTSObject? _promiseConstructorSymbolProperties;

    // The internal superclass view and the public intrinsic are two views of
    // the same Promise constructor. Never recognize a guest name as that owner.
    internal bool TryGetPromiseConstructorSymbolProperties(object? owner, out SharpTSObject properties)
    {
        bool isPublic = owner is SharpTSBuiltInConstructor && ReferenceEquals(owner, PromiseGlobalValue);
        bool isInternal = owner is SharpTSPromiseClass && ReferenceEquals(owner, SharpTSPromiseClass.PromiseBase);
        if (!isPublic && !isInternal)
        {
            properties = null!;
            return false;
        }

        properties = _promiseConstructorSymbolProperties ??= CreatePromiseConstructorSymbolProperties();
        return true;
    }

    private static SharpTSObject CreatePromiseConstructorSymbolProperties()
    {
        var properties = new SharpTSObject([]);
        var getter = new PromiseSpeciesGetter();
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
