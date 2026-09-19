using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Phase 1: Defines the $ReadlineInterface type, fields, constructor, and simple methods.
    /// Must be called BEFORE EmitRuntimeClass so ReadlineCreateInterface can use the constructor.
    /// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSReadlineInterface
    /// </summary>
    private void EmitReadlineInterfaceTypeDefinition(ModuleBuilder moduleBuilder, EmittedReadlineRuntime readline, EmittedEventEmitterRuntime eventEmitter)
    {
        // Define class: public sealed class $ReadlineInterface extends $EventEmitter
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$ReadlineInterface",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            eventEmitter.Type
        );
        readline.InterfaceType = typeBuilder;

        // Fields
        readline.ClosedField = typeBuilder.DefineField("_closed", _types.Boolean, FieldAttributes.Private);
        readline.PausedField = typeBuilder.DefineField("_paused", _types.Boolean, FieldAttributes.Private);
        readline.PromptField = typeBuilder.DefineField("_prompt", _types.String, FieldAttributes.Assembly);

        // Constructor: public $ReadlineInterface()
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        readline.InterfaceCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        // Call base constructor ($EventEmitter)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, eventEmitter.Ctor);
        // _closed = false
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Stfld, readline.ClosedField);
        // _paused = false
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Stfld, readline.PausedField);
        // _prompt = "> "
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldstr, "> ");
        ctorIL.Emit(OpCodes.Stfld, readline.PromptField);
        ctorIL.Emit(OpCodes.Ret);

        // Emit simple methods (don't depend on InvokeValue)
        EmitReadlineInterfaceClose(typeBuilder, readline, eventEmitter);
        EmitReadlineInterfacePrompt(typeBuilder, readline);
        EmitReadlineInterfacePause(typeBuilder, readline, eventEmitter);
        EmitReadlineInterfaceResume(typeBuilder, readline, eventEmitter);
        EmitReadlineInterfaceWrite(typeBuilder, readline);
        EmitReadlineInterfaceSetPrompt(typeBuilder, readline);
        EmitReadlineInterfaceGetPrompt(typeBuilder, readline);

        // NOTE: Question method and CreateType() are deferred to Phase 2
    }

    /// <summary>
    /// Phase 2: Adds the Question method (requires InvokeValue) and finalizes the type.
    /// Must be called AFTER EmitRuntimeClass (Question uses InvokeValue).
    /// </summary>
    private void EmitReadlineInterfaceFinalize(EmittedReadlineRuntime readline, MethodInfo invokeValue)
    {
        // Emit Question method (uses invokeValue)
        EmitReadlineInterfaceQuestion(readline.InterfaceType, readline, invokeValue);

        _ = readline.InterfaceType.CreateType()!;
    }

    /// <summary>
    /// Emits: public object? Question(object query, object callback)
    /// Writes query to console, reads line, invokes callback with answer.
    /// </summary>
    private void EmitReadlineInterfaceQuestion(TypeBuilder typeBuilder, EmittedReadlineRuntime readline, MethodInfo invokeValue)
    {
        var method = typeBuilder.DefineMethod(
            "Question",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var returnNullLabel = il.DefineLabel();

        // if (_closed) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, readline.ClosedField);
        il.Emit(OpCodes.Brtrue, returnNullLabel);

        // var queryStr = query?.ToString() ?? ""
        var queryStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Dup);
        var queryNullLabel = il.DefineLabel();
        var queryDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, queryNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, queryDoneLabel);
        il.MarkLabel(queryNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(queryDoneLabel);
        il.Emit(OpCodes.Stloc, queryStrLocal);

        // Console.Write(queryStr)
        il.Emit(OpCodes.Ldloc, queryStrLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "Write", _types.String));

        // var answer = Console.ReadLine() ?? ""
        var answerLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Console, "ReadLine"));
        var answerNotNull = il.DefineLabel();
        var answerDone = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, answerNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, answerDone);
        il.MarkLabel(answerNotNull);
        il.MarkLabel(answerDone);
        il.Emit(OpCodes.Stloc, answerLocal);

        // if (callback == null) return null;
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // Invoke the callback: InvokeValue(callback, new object[] { answer })
        il.Emit(OpCodes.Ldarg_2);  // callback
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, answerLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, invokeValue);
        il.Emit(OpCodes.Pop);  // Discard return value

        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? Close()
    /// Sets _closed = true, emits 'close' event, and returns this.
    /// </summary>
    private void EmitReadlineInterfaceClose(TypeBuilder typeBuilder, EmittedReadlineRuntime readline, EmittedEventEmitterRuntime eventEmitter)
    {
        var method = typeBuilder.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // _closed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, readline.ClosedField);

        // Emit 'close' event: this.Emit("close", new object[0])
        il.Emit(OpCodes.Ldarg_0);           // this ($ReadlineInterface IS-A $EventEmitter)
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, eventEmitter.Emit);
        il.Emit(OpCodes.Pop);               // discard bool return

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? Prompt(object? preserveCursor)
    /// Writes the stored prompt to console if not closed.
    /// </summary>
    private void EmitReadlineInterfacePrompt(TypeBuilder typeBuilder, EmittedReadlineRuntime readline)
    {
        var method = typeBuilder.DefineMethod(
            "Prompt",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        var returnLabel = il.DefineLabel();

        // if (_closed) goto return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, readline.ClosedField);
        il.Emit(OpCodes.Brtrue, returnLabel);

        // Console.Write(_prompt)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, readline.PromptField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "Write", _types.String));

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? Pause()
    /// </summary>
    private void EmitReadlineInterfacePause(TypeBuilder typeBuilder, EmittedReadlineRuntime readline, EmittedEventEmitterRuntime eventEmitter)
    {
        var method = typeBuilder.DefineMethod(
            "Pause",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, readline.PausedField);

        // Emit 'pause' event: this.Emit("pause", [])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "pause");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, eventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? Resume()
    /// </summary>
    private void EmitReadlineInterfaceResume(TypeBuilder typeBuilder, EmittedReadlineRuntime readline, EmittedEventEmitterRuntime eventEmitter)
    {
        var method = typeBuilder.DefineMethod(
            "Resume",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, readline.PausedField);

        // Emit 'resume' event: this.Emit("resume", [])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "resume");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, eventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? Write(object data)
    /// </summary>
    private void EmitReadlineInterfaceWrite(TypeBuilder typeBuilder, EmittedReadlineRuntime readline)
    {
        var method = typeBuilder.DefineMethod(
            "Write",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var returnLabel = il.DefineLabel();

        // if (_closed) goto return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, readline.ClosedField);
        il.Emit(OpCodes.Brtrue, returnLabel);

        // Console.Write(data?.ToString() ?? "")
        il.Emit(OpCodes.Ldarg_1);
        var notNullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "Write", _types.String));

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? SetPrompt(object prompt)
    /// </summary>
    private void EmitReadlineInterfaceSetPrompt(TypeBuilder typeBuilder, EmittedReadlineRuntime readline)
    {
        var method = typeBuilder.DefineMethod(
            "SetPrompt",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        var notNullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Stfld, readline.PromptField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? GetPrompt()
    /// </summary>
    private void EmitReadlineInterfaceGetPrompt(TypeBuilder typeBuilder, EmittedReadlineRuntime readline)
    {
        var method = typeBuilder.DefineMethod(
            "GetPrompt",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, readline.PromptField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits readline module helper methods.
    /// </summary>
    private void EmitReadlineMethods(TypeBuilder typeBuilder, EmittedReadlineRuntime readline)
    {
        // The caller supplies the required declarations after selecting readline.
        EmitReadlineQuestionSync(typeBuilder, readline);
        EmitReadlineCreateInterface(typeBuilder, readline);
    }

    /// <summary>
    /// Emits: public static string ReadlineQuestionSync(string query)
    /// </summary>
    private void EmitReadlineQuestionSync(TypeBuilder typeBuilder, EmittedReadlineRuntime readline)
    {
        var method = typeBuilder.DefineMethod(
            "ReadlineQuestionSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String]);
        readline.QuestionSync = method;
        // No EmittedBuiltInModuleRegistry.Register — `readline` is now a TS stdlib module
        // (stdlib/node/readline.ts) that calls primitive:readline. CJS
        // require('readline') flows through the standard ESM→CJS namespace-object path.

        var il = method.GetILGenerator();

        // Console.Write(query)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "Write", _types.String));

        // return Console.ReadLine() ?? ""
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Console, "ReadLine"));

        var notNull = il.DefineLabel();
        var end = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, end);
        il.MarkLabel(notNull);
        il.MarkLabel(end);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ReadlineCreateInterface(object options)
    /// Creates a new $ReadlineInterface instance (pure IL, no SharpTS.dll dependency).
    /// If options is a Dictionary with a "prompt" key, sets it on the new instance.
    /// </summary>
    private void EmitReadlineCreateInterface(TypeBuilder typeBuilder, EmittedReadlineRuntime readline)
    {
        var method = typeBuilder.DefineMethod(
            "ReadlineCreateInterface",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        readline.CreateInterface = method;

        var il = method.GetILGenerator();

        // var rl = new $ReadlineInterface();
        var rlLocal = il.DeclareLocal(readline.InterfaceType);
        il.Emit(OpCodes.Newobj, readline.InterfaceCtor);
        il.Emit(OpCodes.Stloc, rlLocal);

        // Check if options is Dictionary<string, object?>
        var skipPrompt = il.DefineLabel();
        var dictType = typeof(Dictionary<string, object?>);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, skipPrompt);

        // Try to get "prompt" from options dict
        var promptLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "prompt");
        il.Emit(OpCodes.Ldloca, promptLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brfalse, skipPrompt);

        // If found and non-null, set rl._prompt = prompt.ToString()
        il.Emit(OpCodes.Ldloc, promptLocal);
        il.Emit(OpCodes.Brfalse, skipPrompt);
        il.Emit(OpCodes.Ldloc, rlLocal);
        il.Emit(OpCodes.Ldloc, promptLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stfld, readline.PromptField);

        il.MarkLabel(skipPrompt);

        // return rl;
        il.Emit(OpCodes.Ldloc, rlLocal);
        il.Emit(OpCodes.Ret);
    }
}
