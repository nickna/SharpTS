using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public sealed partial class ValidatedILBuilder
{
    #region Call Operations

    /// <summary>
    /// Calls a method.
    /// </summary>
    public void Emit_Call(MethodInfo method)
    {

        var paramCount = method.GetParameters().Length;
        if (!method.IsStatic) paramCount++; // Include 'this'

        RequireStackDepth(paramCount, "Call");

        for (int i = 0; i < paramCount; i++)
            PopStack();

        _il.Emit(OpCodes.Call, method);

        if (method.ReturnType != typeof(void))
            PushStack(GetStackEntryType(method.ReturnType), method.ReturnType);
    }

    /// <summary>
    /// Calls a method virtually.
    /// </summary>
    public void Emit_Callvirt(MethodInfo method)
    {

        var paramCount = method.GetParameters().Length + 1; // Always has 'this'

        RequireStackDepth(paramCount, "Callvirt");

        for (int i = 0; i < paramCount; i++)
            PopStack();

        _il.Emit(OpCodes.Callvirt, method);

        if (method.ReturnType != typeof(void))
            PushStack(GetStackEntryType(method.ReturnType), method.ReturnType);
    }

    /// <summary>
    /// Creates a new object using a constructor.
    /// </summary>
    public void Emit_Newobj(ConstructorInfo ctor)
    {

        var paramCount = ctor.GetParameters().Length;

        RequireStackDepth(paramCount, "Newobj");

        for (int i = 0; i < paramCount; i++)
            PopStack();

        _il.Emit(OpCodes.Newobj, ctor);
        PushStack(StackEntryType.Reference, ctor.DeclaringType);
    }

    #endregion

    #region Boxing Operations

    /// <summary>
    /// Boxes a value type.
    /// </summary>
    /// <param name="type">The value type to box.</param>
    /// <exception cref="ILValidationException">Thrown if top of stack is not a value type.</exception>
    public void Emit_Box(Type type)
    {

        RequireStackDepth(1, "Box");

        var top = PeekStack();
        if (!top.IsValueType && top.Type != StackEntryType.Unknown)
        {
            ThrowOrRecord($"Box requires value type on stack, found {top.Type}");
        }

        PopStack();
        PushStack(StackEntryType.Reference, type);
        _il.Emit(OpCodes.Box, type);
    }

    /// <summary>
    /// Unboxes to any type (value type or nullable).
    /// </summary>
    public void Emit_Unbox_Any(Type type)
    {

        RequireStackDepth(1, "Unbox_Any");

        var top = PeekStack();
        if (top.IsValueType)
        {
            ThrowOrRecord($"Unbox_Any requires reference type on stack, found {top.Type}");
        }

        PopStack();
        PushStack(GetStackEntryType(type), type);
        _il.Emit(OpCodes.Unbox_Any, type);
    }

    #endregion

    #region Type Operations

    /// <summary>
    /// Checks if an object is an instance of a type.
    /// </summary>
    public void Emit_Isinst(Type type)
    {

        RequireStackDepth(1, "Isinst");
        PopStack();
        _il.Emit(OpCodes.Isinst, type);
        PushStack(StackEntryType.Reference, type);
    }

    /// <summary>
    /// Casts to a class type.
    /// </summary>
    public void Emit_Castclass(Type type)
    {

        RequireStackDepth(1, "Castclass");
        PopStack();
        _il.Emit(OpCodes.Castclass, type);
        PushStack(StackEntryType.Reference, type);
    }

    /// <summary>
    /// Declares a local variable (passthrough to ILGenerator).
    /// </summary>
    public LocalBuilder DeclareLocal(Type type)
    {
        return _il.DeclareLocal(type);
    }

    #endregion
}
