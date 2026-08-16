using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: public static (FileMode, FileAccess, FileShare) FsFlagsParsePure(object flags)
    /// Pure-IL implementation that doesn't require SharpTS.dll reflection.
    /// Parses Node.js file flags (string or numeric) to .NET enums.
    /// </summary>
    private void EmitFsFlagsParsePureHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Return type is ValueTuple<FileMode, FileAccess, FileShare>
        var tupleType = typeof(ValueTuple<FileMode, FileAccess, FileShare>);

        var method = typeBuilder.DefineMethod(
            "FsFlagsParsePure",
            MethodAttributes.Public | MethodAttributes.Static,
            tupleType,
            [_types.Object]
        );
        runtime.FsFlagsParsePure = method;

        var il = method.GetILGenerator();

        // Locals
        var resultLocal = il.DeclareLocal(tupleType);
        var flagStrLocal = il.DeclareLocal(_types.String);
        var accessBitsLocal = il.DeclareLocal(_types.Int32);
        var flagsIntLocal = il.DeclareLocal(_types.Int32);

        // Labels
        var checkIntLabel = il.DefineLabel();
        var parseNumericLabel = il.DefineLabel();
        var parseStringLabel = il.DefineLabel();
        var returnDefaultLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();

        // String flag labels
        var checkR = il.DefineLabel();
        var checkRsOrSr = il.DefineLabel();
        var checkRPlus = il.DefineLabel();
        var checkRsPlusOrSrPlus = il.DefineLabel();
        var checkW = il.DefineLabel();
        var checkWxOrXw = il.DefineLabel();
        var checkWPlus = il.DefineLabel();
        var checkWxPlusOrXwPlus = il.DefineLabel();
        var checkA = il.DefineLabel();
        var checkAxOrXa = il.DefineLabel();
        var checkAPlus = il.DefineLabel();
        var checkAxPlusOrXaPlus = il.DefineLabel();
        var defaultCase = il.DefineLabel();

        // Check if flags is double
        // if (flags is double d) goto parseNumeric with (int)d
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, checkIntLabel);

        // It's a double - convert to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, flagsIntLocal);
        il.Emit(OpCodes.Br, parseNumericLabel);

        // Check if flags is int
        il.MarkLabel(checkIntLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(int));
        il.Emit(OpCodes.Brfalse, parseStringLabel);

        // It's an int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(int));
        il.Emit(OpCodes.Stloc, flagsIntLocal);
        il.Emit(OpCodes.Br, parseNumericLabel);

        // Parse string
        il.MarkLabel(parseStringLabel);
        // var flagStr = flags?.ToString() ?? "r";
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnDefaultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, flagStrLocal);

        // String switch using if-else chain
        // "r" => (FileMode.Open, FileAccess.Read, FileShare.Read)
        EmitStringCheck(il, flagStrLocal, "r", checkRsOrSr);
        EmitCreateTuple(il, resultLocal, FileMode.Open, FileAccess.Read, FileShare.Read);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "rs" or "sr"
        il.MarkLabel(checkRsOrSr);
        EmitStringCheck(il, flagStrLocal, "rs", checkR);
        EmitCreateTuple(il, resultLocal, FileMode.Open, FileAccess.Read, FileShare.Read);
        il.Emit(OpCodes.Br, returnResultLabel);

        il.MarkLabel(checkR);
        EmitStringCheck(il, flagStrLocal, "sr", checkRPlus);
        EmitCreateTuple(il, resultLocal, FileMode.Open, FileAccess.Read, FileShare.Read);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "r+"
        il.MarkLabel(checkRPlus);
        EmitStringCheck(il, flagStrLocal, "r+", checkRsPlusOrSrPlus);
        EmitCreateTuple(il, resultLocal, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "rs+" or "sr+"
        il.MarkLabel(checkRsPlusOrSrPlus);
        EmitStringCheck(il, flagStrLocal, "rs+", checkW);
        EmitCreateTuple(il, resultLocal, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        il.Emit(OpCodes.Br, returnResultLabel);

        il.MarkLabel(checkW);
        EmitStringCheck(il, flagStrLocal, "sr+", checkWxOrXw);
        EmitCreateTuple(il, resultLocal, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "w"
        il.MarkLabel(checkWxOrXw);
        EmitStringCheck(il, flagStrLocal, "w", checkWPlus);
        EmitCreateTuple(il, resultLocal, FileMode.Create, FileAccess.Write, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "wx" or "xw"
        il.MarkLabel(checkWPlus);
        EmitStringCheck(il, flagStrLocal, "wx", checkWxPlusOrXwPlus);
        EmitCreateTuple(il, resultLocal, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        il.MarkLabel(checkWxPlusOrXwPlus);
        EmitStringCheck(il, flagStrLocal, "xw", checkA);
        EmitCreateTuple(il, resultLocal, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "w+"
        il.MarkLabel(checkA);
        EmitStringCheck(il, flagStrLocal, "w+", checkAxOrXa);
        EmitCreateTuple(il, resultLocal, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "wx+" or "xw+"
        il.MarkLabel(checkAxOrXa);
        EmitStringCheck(il, flagStrLocal, "wx+", checkAPlus);
        EmitCreateTuple(il, resultLocal, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        il.MarkLabel(checkAPlus);
        EmitStringCheck(il, flagStrLocal, "xw+", checkAxPlusOrXaPlus);
        EmitCreateTuple(il, resultLocal, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "a"
        il.MarkLabel(checkAxPlusOrXaPlus);
        EmitStringCheck(il, flagStrLocal, "a", defaultCase);
        EmitCreateTuple(il, resultLocal, FileMode.Append, FileAccess.Write, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "ax" or "xa"
        il.MarkLabel(defaultCase);
        var afterAxCheck = il.DefineLabel();
        EmitStringCheck(il, flagStrLocal, "ax", afterAxCheck);
        EmitCreateTuple(il, resultLocal, FileMode.Append, FileAccess.Write, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        il.MarkLabel(afterAxCheck);
        var afterXaCheck = il.DefineLabel();
        EmitStringCheck(il, flagStrLocal, "xa", afterXaCheck);
        EmitCreateTuple(il, resultLocal, FileMode.Append, FileAccess.Write, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "a+"
        il.MarkLabel(afterXaCheck);
        var afterAPlusCheck = il.DefineLabel();
        EmitStringCheck(il, flagStrLocal, "a+", afterAPlusCheck);
        EmitCreateTuple(il, resultLocal, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        // "ax+" or "xa+"
        il.MarkLabel(afterAPlusCheck);
        var afterAxPlusCheck = il.DefineLabel();
        EmitStringCheck(il, flagStrLocal, "ax+", afterAxPlusCheck);
        EmitCreateTuple(il, resultLocal, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        il.MarkLabel(afterAxPlusCheck);
        var afterXaPlusCheck = il.DefineLabel();
        EmitStringCheck(il, flagStrLocal, "xa+", afterXaPlusCheck);
        EmitCreateTuple(il, resultLocal, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        il.Emit(OpCodes.Br, returnResultLabel);

        // Default: (FileMode.Open, FileAccess.Read, FileShare.Read)
        il.MarkLabel(afterXaPlusCheck);
        il.MarkLabel(returnDefaultLabel);
        EmitCreateTuple(il, resultLocal, FileMode.Open, FileAccess.Read, FileShare.Read);
        il.Emit(OpCodes.Br, returnResultLabel);

        // Parse numeric flags
        il.MarkLabel(parseNumericLabel);

        // Node.js numeric flags constants (O_RDONLY = 0 is implicit in default case)
        const int O_WRONLY = 1;
        const int O_RDWR = 2;
        const int O_CREAT = 64;
        const int O_EXCL = 128;
        const int O_TRUNC = 512;
        const int O_APPEND = 1024;

        // var accessBits = flags & 3
        il.Emit(OpCodes.Ldloc, flagsIntLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Stloc, accessBitsLocal);

        // Determine FileAccess based on accessBits
        var accessWriteOnly = il.DefineLabel();
        var accessReadWrite = il.DefineLabel();
        var afterAccessLabel = il.DefineLabel();
        var fileModeLocal = il.DeclareLocal(typeof(FileMode));
        var fileAccessLocal = il.DeclareLocal(typeof(FileAccess));
        var fileShareLocal = il.DeclareLocal(typeof(FileShare));

        // switch (accessBits)
        il.Emit(OpCodes.Ldloc, accessBitsLocal);
        il.Emit(OpCodes.Ldc_I4, O_WRONLY);
        il.Emit(OpCodes.Beq, accessWriteOnly);

        il.Emit(OpCodes.Ldloc, accessBitsLocal);
        il.Emit(OpCodes.Ldc_I4, O_RDWR);
        il.Emit(OpCodes.Beq, accessReadWrite);

        // default: Read
        il.Emit(OpCodes.Ldc_I4, (int)FileAccess.Read);
        il.Emit(OpCodes.Stloc, fileAccessLocal);
        il.Emit(OpCodes.Br, afterAccessLabel);

        il.MarkLabel(accessWriteOnly);
        il.Emit(OpCodes.Ldc_I4, (int)FileAccess.Write);
        il.Emit(OpCodes.Stloc, fileAccessLocal);
        il.Emit(OpCodes.Br, afterAccessLabel);

        il.MarkLabel(accessReadWrite);
        il.Emit(OpCodes.Ldc_I4, (int)FileAccess.ReadWrite);
        il.Emit(OpCodes.Stloc, fileAccessLocal);

        il.MarkLabel(afterAccessLabel);

        // Determine FileMode
        var checkO_TRUNC = il.DefineLabel();
        var checkO_APPEND = il.DefineLabel();
        var checkO_EXCL = il.DefineLabel();
        var checkO_TRUNC_CREAT = il.DefineLabel();
        var modeOpenOrCreate = il.DefineLabel();
        var modeOpen = il.DefineLabel();
        var afterModeLabel = il.DefineLabel();

        // if ((flags & O_CREAT) != 0)
        il.Emit(OpCodes.Ldloc, flagsIntLocal);
        il.Emit(OpCodes.Ldc_I4, O_CREAT);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, checkO_TRUNC);

        // O_CREAT is set
        // if ((flags & O_EXCL) != 0) mode = CreateNew
        il.Emit(OpCodes.Ldloc, flagsIntLocal);
        il.Emit(OpCodes.Ldc_I4, O_EXCL);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, checkO_TRUNC_CREAT);

        il.Emit(OpCodes.Ldc_I4, (int)FileMode.CreateNew);
        il.Emit(OpCodes.Stloc, fileModeLocal);
        il.Emit(OpCodes.Br, afterModeLabel);

        // else if ((flags & O_TRUNC) != 0) mode = Create
        il.MarkLabel(checkO_TRUNC_CREAT);
        il.Emit(OpCodes.Ldloc, flagsIntLocal);
        il.Emit(OpCodes.Ldc_I4, O_TRUNC);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, modeOpenOrCreate);

        il.Emit(OpCodes.Ldc_I4, (int)FileMode.Create);
        il.Emit(OpCodes.Stloc, fileModeLocal);
        il.Emit(OpCodes.Br, afterModeLabel);

        // else mode = OpenOrCreate
        il.MarkLabel(modeOpenOrCreate);
        il.Emit(OpCodes.Ldc_I4, (int)FileMode.OpenOrCreate);
        il.Emit(OpCodes.Stloc, fileModeLocal);
        il.Emit(OpCodes.Br, afterModeLabel);

        // O_CREAT not set
        il.MarkLabel(checkO_TRUNC);
        // if ((flags & O_TRUNC) != 0) mode = Truncate
        il.Emit(OpCodes.Ldloc, flagsIntLocal);
        il.Emit(OpCodes.Ldc_I4, O_TRUNC);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, checkO_APPEND);

        il.Emit(OpCodes.Ldc_I4, (int)FileMode.Truncate);
        il.Emit(OpCodes.Stloc, fileModeLocal);
        il.Emit(OpCodes.Br, afterModeLabel);

        // else if ((flags & O_APPEND) != 0) mode = Append
        il.MarkLabel(checkO_APPEND);
        il.Emit(OpCodes.Ldloc, flagsIntLocal);
        il.Emit(OpCodes.Ldc_I4, O_APPEND);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, modeOpen);

        il.Emit(OpCodes.Ldc_I4, (int)FileMode.Append);
        il.Emit(OpCodes.Stloc, fileModeLocal);
        il.Emit(OpCodes.Br, afterModeLabel);

        // else mode = Open
        il.MarkLabel(modeOpen);
        il.Emit(OpCodes.Ldc_I4, (int)FileMode.Open);
        il.Emit(OpCodes.Stloc, fileModeLocal);

        il.MarkLabel(afterModeLabel);

        // FileShare = ReadWrite for numeric flags
        il.Emit(OpCodes.Ldc_I4, (int)FileShare.ReadWrite);
        il.Emit(OpCodes.Stloc, fileShareLocal);

        // Create result tuple
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Ldloc, fileModeLocal);
        il.Emit(OpCodes.Ldloc, fileAccessLocal);
        il.Emit(OpCodes.Ldloc, fileShareLocal);
        var tupleCtor = tupleType.GetConstructor([typeof(FileMode), typeof(FileAccess), typeof(FileShare)])!;
        il.Emit(OpCodes.Call, tupleCtor);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a string equality check and branch if not equal.
    /// </summary>
    private void EmitStringCheck(ILGenerator il, LocalBuilder strLocal, string value, Label notEqualLabel)
    {
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Equals", _types.String));
        il.Emit(OpCodes.Brfalse, notEqualLabel);
    }

    /// <summary>
    /// Emits code to create a ValueTuple&lt;FileMode, FileAccess, FileShare&gt;.
    /// </summary>
    private void EmitCreateTuple(ILGenerator il, LocalBuilder resultLocal, FileMode mode, FileAccess access, FileShare share)
    {
        var tupleType = typeof(ValueTuple<FileMode, FileAccess, FileShare>);
        var tupleCtor = tupleType.GetConstructor([typeof(FileMode), typeof(FileAccess), typeof(FileShare)])!;

        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Ldc_I4, (int)mode);
        il.Emit(OpCodes.Ldc_I4, (int)access);
        il.Emit(OpCodes.Ldc_I4, (int)share);
        il.Emit(OpCodes.Call, tupleCtor);
    }
}
