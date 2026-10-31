using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private RuntimeValue IncrementPrivateField(object? receiver, string name, double delta, bool returnOld)
    {
        var owner = _environment.FindPrivateClass(name);
        if (owner is null)
            throw new ThrowException(new SharpTSTypeError($"Private member {name} is not declared in this scope"));
        bool isStatic = owner.HasStaticPrivateField(name);
        if (isStatic ? !ReferenceEquals(receiver, owner) || !owner.HasInstalledStaticPrivateField(name)
            : receiver is null || !owner.HasInstalledPrivateField(receiver, name))
            throw new ThrowException(new SharpTSTypeError($"Cannot access private member {name} from an object whose class did not declare it or before initialization"));

        RuntimeValue current = isStatic ? owner.GetStaticPrivateFieldRV(name) : owner.GetPrivateFieldRV(receiver!, name);
        // Retain both the lexical owner and receiver before observable conversion.
        RuntimeValue oldValue = ToNumericRV(current);
        RuntimeValue newValue = oldValue.IsBigInt
            ? RuntimeValue.FromBigInt(new SharpTSBigInt(oldValue.AsBigInt().Value + (delta > 0 ? 1 : -1)))
            : RuntimeValue.FromNumber(oldValue.AsNumber() + delta);
        if (isStatic) owner.SetStaticPrivateField(name, newValue.ToObject());
        else owner.SetPrivateField(receiver!, name, newValue.ToObject());
        return returnOld ? oldValue : newValue;
    }
}
