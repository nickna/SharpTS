using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    // The reference-type iterator state-machine creation stubs, shared between the sync generator and
    // async generator families (#1126). Both build byte-identical stubs — the only prior difference was
    // the static `smBuilder` type, now abstracted behind IIteratorStateMachineBuilder — so a single free
    // -function stub and a single method (instance/static) stub replace the six near-identical copies.
    // The async *function* stub stays separate: its state machine is a value-type struct initialised via
    // ldloca/initobj with an AsyncTaskMethodBuilder, a fundamentally different shape.

    /// <summary>
    /// Emits the creation stub for a top-level (free-function) iterator: news up the reference-type state
    /// machine, snapshots the active dynamic <c>this</c> from the thread-local receiver into
    /// <c>&lt;&gt;4__this</c> (#775 — the stub runs eagerly when the iterator is created, but MoveNext
    /// runs lazily, by which time the thread-local receiver is gone), copies the parameters into their
    /// object-typed hoisted fields (no boxing — free-function stub slots are already <c>object</c>), seeds
    /// the function display class, and returns the state machine.
    /// </summary>
    private void EmitIteratorFreeFunctionStub(
        MethodBuilder methodBuilder,
        IIteratorStateMachineBuilder smBuilder,
        Stmt.Function funcStmt,
        string qualifiedName)
    {
        var il = methodBuilder.GetILGenerator();

        // Parameter initialization belongs to the generator call, before the
        // iterator is returned, rather than to its first MoveNext invocation.
        var precreatedFunctionDC = EmitIteratorStubDefaults(
            methodBuilder, funcStmt, isInstanceMethod: false, funcDCKey: qualifiedName);

        il.Emit(OpCodes.Newobj, smBuilder.Constructor);

        // #775: capture the active dynamic `this` into <>4__this. The receiver is the thread-local
        // `$TSFunction._currentFunctionThis` (set by InvokeWithThis for an `o.gen()` / `.call(recv)`
        // value-call), coerced null → globalThis sentinel to match LocalVariableResolver.LoadThis.
        if (smBuilder.ThisField != null && _runtime?.CurrentFunctionThisField != null)
        {
            il.Emit(OpCodes.Dup);       // Keep state machine reference on stack
            il.Emit(OpCodes.Ldsfld, _runtime.CurrentFunctionThisField);
            if (_runtime.GlobalThisSingletonField != null)
            {
                var thisNotNull = il.DefineLabel();
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brtrue, thisNotNull);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldsfld, _runtime.GlobalThisSingletonField);
                il.MarkLabel(thisNotNull);
            }
            il.Emit(OpCodes.Stfld, smBuilder.ThisField);
        }

        // Copy parameters to state machine fields. Free-function stub params are object-typed
        // (BuildStateMachineStubParamTypes), so no boxing is needed.
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            var field = smBuilder.GetVariableField(funcStmt.Parameters[i].Name.Lexeme);
            if (field != null)
            {
                il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
                il.Emit(OpCodes.Ldarg, i);
                il.Emit(OpCodes.Stfld, field);
            }
        }

        // Instantiate the function display class (#674/#725) and seed every captured parameter it owns
        // so nested closures share the iterator's live storage. Captured outer-scope variables
        // are NOT copied here — snapshotting them at creation hid later mutations (#541); MoveNext reads
        // them live from their enclosing storage instead.
        EmitGeneratorFunctionDCInit(
            il, smBuilder.FunctionDCField, funcStmt, qualifiedName, paramOffset: 0,
            precreatedFunctionDC: precreatedFunctionDC);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the creation stub for an iterator *method* (instance or static). Like the free-function
    /// stub but takes <c>this</c> from arg 0 for instance methods (params then start at arg 1) and boxes
    /// value-type parameters into the object-typed fields, deciding from the method's ACTUAL IL signature
    /// (<see cref="MethodBuilder.GetParameters"/>): a private method's parameters are all <c>object</c>
    /// slots, so boxing the AST-resolved value type would mismatch the loaded <c>object</c>
    /// (StackUnexpected). The function DC is seeded only when <paramref name="funcDCKey"/> is non-null;
    /// static iterator methods use the same path with parameter argument offset zero.
    /// </summary>
    private void EmitIteratorMethodStub(
        MethodBuilder methodBuilder,
        IIteratorStateMachineBuilder smBuilder,
        Stmt.Function method,
        bool isInstanceMethod,
        string? funcDCKey,
        FieldInfo? fieldsField,
        string? currentClassName)
    {
        var parameters = method.Parameters;
        var il = methodBuilder.GetILGenerator();

        var precreatedFunctionDC = EmitIteratorStubDefaults(
            methodBuilder,
            method,
            isInstanceMethod,
            fieldsField,
            currentClassName,
            funcDCKey);

        il.Emit(OpCodes.Newobj, smBuilder.Constructor);

        if (isInstanceMethod && smBuilder.ThisField != null)
        {
            il.Emit(OpCodes.Dup);      // Keep state machine reference on stack
            il.Emit(OpCodes.Ldarg_0);  // 'this' is at arg 0 for instance methods
            il.Emit(OpCodes.Stfld, smBuilder.ThisField);
        }

        var paramTypes = methodBuilder.GetParameters();
        int paramOffset = isInstanceMethod ? 1 : 0;

        for (int i = 0; i < parameters.Count; i++)
        {
            var field = smBuilder.GetVariableField(parameters[i].Name.Lexeme);
            if (field != null)
            {
                il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
                il.Emit(OpCodes.Ldarg, i + paramOffset);
                if (i < paramTypes.Length && paramTypes[i].ParameterType.IsValueType)
                    il.Emit(OpCodes.Box, paramTypes[i].ParameterType);
                il.Emit(OpCodes.Stfld, field);
            }
        }

        // Seed captured parameters into the function DC (typed stub params → value types are boxed
        // before the store). No-op when the method has no function DC.
        if (funcDCKey != null)
            EmitGeneratorFunctionDCInit(
                il, smBuilder.FunctionDCField, method, funcDCKey, paramOffset, paramTypes,
                precreatedFunctionDC);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits generator and async-generator parameter initialization into their eager
    /// creation stub. Defaults update the argument slots before those values are copied
    /// into the state machine and any function display class.
    /// </summary>
    private LocalBuilder? EmitIteratorStubDefaults(
        MethodBuilder methodBuilder,
        Stmt.Function function,
        bool isInstanceMethod,
        FieldInfo? fieldsField = null,
        string? currentClassName = null,
        string? funcDCKey = null)
    {
        if (!function.Parameters.Any(p => p.DefaultValue != null))
            return null;

        var ctx = CreateModuleMemberContext(methodBuilder.GetILGenerator(), methodBuilder);
        ctx.FieldsField = fieldsField;
        ctx.IsInstanceMethod = isInstanceMethod;
        ctx.IsStrictMode = _isStrictMode || BodyDeclaresUseStrict(function.Body);
        ctx.CurrentClassName = currentClassName;
        ctx.CurrentClassBuilder = methodBuilder.DeclaringType as TypeBuilder;
        ctx.EmittingTypeBuilder = methodBuilder.DeclaringType as TypeBuilder;
        ApplyCapturedTopLevelVariableAccess(ctx);
        ApplyCommonJsModuleAccess(ctx);

        LocalBuilder? functionDCLocal = null;
        if (funcDCKey != null
            && _closures.FunctionDisplayClasses.TryGetValue(funcDCKey, out var functionDCType)
            && _closures.FunctionDisplayClassCtors.TryGetValue(funcDCKey, out var functionDCCtor)
            && _closures.FunctionDisplayClassFields.TryGetValue(funcDCKey, out var functionDCFields))
        {
            functionDCLocal = methodBuilder.GetILGenerator().DeclareLocal(functionDCType);
            methodBuilder.GetILGenerator().Emit(OpCodes.Newobj, functionDCCtor);
            methodBuilder.GetILGenerator().Emit(OpCodes.Stloc, functionDCLocal);
            ctx.FunctionDisplayClassLocal = functionDCLocal;
            ctx.FunctionDisplayClassFields = functionDCFields;
            ctx.CapturedFunctionLocals = [.. functionDCFields.Keys];
            ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0
                ? _closures.ArrowFunctionDCFields
                : null;
        }

        var methodParams = methodBuilder.GetParameters();
        int paramOffset = isInstanceMethod ? 1 : 0;
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            Type? paramType = i < methodParams.Length ? methodParams[i].ParameterType : null;
            ctx.DefineParameter(function.Parameters[i].Name.Lexeme, i + paramOffset, paramType);
        }

        new ILEmitter(ctx).EmitDefaultParameters(
            function.Parameters,
            isInstanceMethod,
            paramTypes: methodParams.Select(p => p.ParameterType).ToArray());

        return functionDCLocal;
    }
}
