using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public sealed partial class ValidatedILBuilder
{
    #region Load Operations

    /// <summary>
    /// Loads a 32-bit integer constant.
    /// </summary>
    public void Emit_Ldc_I4(int value)
    {

        _il.Emit(OpCodes.Ldc_I4, value);
        PushStack(StackEntryType.Int32);
    }

    /// <summary>
    /// Loads a 64-bit floating point constant.
    /// </summary>
    public void Emit_Ldc_R8(double value)
    {

        _il.Emit(OpCodes.Ldc_R8, value);
        PushStack(StackEntryType.Double);
    }

    /// <summary>
    /// Loads a string constant.
    /// </summary>
    public void Emit_Ldstr(string value)
    {

        _il.Emit(OpCodes.Ldstr, value);
        PushStack(StackEntryType.String);
    }

    /// <summary>
    /// Loads a null reference.
    /// </summary>
    public void Emit_Ldnull()
    {

        _il.Emit(OpCodes.Ldnull);
        PushStack(StackEntryType.Null);
    }

    /// <summary>
    /// Loads a local variable.
    /// </summary>
    public void Emit_Ldloc(LocalBuilder local)
    {

        _il.Emit(OpCodes.Ldloc, local);
        PushStack(GetStackEntryType(local.LocalType), local.LocalType);
    }

    /// <summary>
    /// Loads a local variable by index.
    /// </summary>
    public void Emit_Ldloc(int index)
    {

        _il.Emit(OpCodes.Ldloc, index);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Loads an argument.
    /// </summary>
    public void Emit_Ldarg(int index)
    {

        _il.Emit(OpCodes.Ldarg, index);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Loads a field value.
    /// </summary>
    public void Emit_Ldfld(FieldInfo field)
    {

        RequireStackDepth(1, "Ldfld");
        PopStack(); // Object reference
        _il.Emit(OpCodes.Ldfld, field);
        PushStack(GetStackEntryType(field.FieldType), field.FieldType);
    }

    /// <summary>
    /// Loads a static field value.
    /// </summary>
    public void Emit_Ldsfld(FieldInfo field)
    {

        _il.Emit(OpCodes.Ldsfld, field);
        PushStack(GetStackEntryType(field.FieldType), field.FieldType);
    }

    #endregion

    #region Store Operations

    /// <summary>
    /// Stores a value into a local variable.
    /// </summary>
    public void Emit_Stloc(LocalBuilder local)
    {

        RequireStackDepth(1, "Stloc");
        PopStack();
        _il.Emit(OpCodes.Stloc, local);
    }

    /// <summary>
    /// Stores a value into a local variable by index.
    /// </summary>
    public void Emit_Stloc(int index)
    {

        RequireStackDepth(1, "Stloc");
        PopStack();
        _il.Emit(OpCodes.Stloc, index);
    }

    /// <summary>
    /// Stores a value into an argument.
    /// </summary>
    public void Emit_Starg(int index)
    {

        RequireStackDepth(1, "Starg");
        PopStack();
        _il.Emit(OpCodes.Starg, index);
    }

    /// <summary>
    /// Stores a value into a field.
    /// </summary>
    public void Emit_Stfld(FieldInfo field)
    {

        RequireStackDepth(2, "Stfld");
        PopStack(); // Value
        PopStack(); // Object reference
        _il.Emit(OpCodes.Stfld, field);
    }

    /// <summary>
    /// Stores a value into a static field.
    /// </summary>
    public void Emit_Stsfld(FieldInfo field)
    {

        RequireStackDepth(1, "Stsfld");
        PopStack();
        _il.Emit(OpCodes.Stsfld, field);
    }

    #endregion
}
