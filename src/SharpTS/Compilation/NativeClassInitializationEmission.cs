using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Emits declaration-site calls whose native class-initializer targets are literal closed types.</summary>
internal sealed class NativeClassInitializationEmission(
    TypeBuilder program, TypeProvider types, ClassGenericParameterRegistry parameters,
    NativeClosedSourceFamilies? families = null)
{
    private int _nextStub;
    internal int LegacyFallbackCount { get; private set; }
    internal bool IsComplete { get; private set; }

    internal bool TryEmit(ILGenerator destination, Type requestedOwner)
    {
        if (IsComplete || program.IsCreated())
            throw new InvalidOperationException("Native class initialization emission is complete.");
        Type owner = requestedOwner;
        if (owner.IsGenericTypeDefinition)
        {
            if (owner is not TypeBuilder definition ||
                !parameters.TryGetCanonicalArguments(definition, out var arguments) ||
                arguments.Any(FunctionGenericParameterRegistry.ContainsUnboundType))
                return UseLegacy();
            owner = EmitGenerics.MakeGenericType(definition, arguments.ToArray());
        }
        Type definitionOwner = owner.IsGenericType ? owner.GetGenericTypeDefinition() : owner;
        if (FunctionGenericParameterRegistry.ContainsUnboundType(owner) ||
            definitionOwner is not TypeBuilder builder || builder.Module.ModuleVersionId != program.Module.ModuleVersionId)
            return UseLegacy();

        // A call-site stub keeps the caller's surrounding expression stack out
        // of this exception region. Defining it does not run any initializer.
        var stub = program.DefineMethod($"<>NativeInitializeClass{_nextStub++}",
            MethodAttributes.Assembly | MethodAttributes.Static, types.Void, Type.EmptyTypes);
        var il = stub.GetILGenerator();
        var exception = il.DeclareLocal(typeof(TypeInitializationException));
        var done = il.DefineLabel();
        var throwInner = il.DefineLabel();
        il.BeginExceptionBlock();
        int ownerOffset = il.ILOffset;
        il.Emit(OpCodes.Ldtoken, owner);
        il.Emit(OpCodes.Call, types.RuntimeHelpersRunClassConstructor);
        il.Emit(OpCodes.Leave, done);
        il.BeginCatchBlock(typeof(TypeInitializationException));
        il.Emit(OpCodes.Stloc, exception);
        il.Emit(OpCodes.Ldloc, exception);
        il.Emit(OpCodes.Callvirt, types.GetPropertyGetter(types.Exception, "InnerException"));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, throwInner);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, exception);
        il.MarkLabel(throwInner);
        il.Emit(OpCodes.Throw);
        il.EndExceptionBlock();
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
        families?.RecordInitializerOperand(owner, stub, ownerOffset);
        destination.Emit(OpCodes.Call, stub);
        return true;

        bool UseLegacy()
        {
            families?.RecordUnprovedInitializer(requestedOwner);
            LegacyFallbackCount++;
            return false;
        }
    }

    internal void CompleteEmission()
    {
        if (IsComplete) throw new InvalidOperationException("Native class initialization emission is already complete.");
        IsComplete = true;
    }
}
