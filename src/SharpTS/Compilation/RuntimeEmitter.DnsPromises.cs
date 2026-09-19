using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits dns/promises support methods into the $Runtime class.
/// Pure IL — calls existing emitted DNS sync methods (DnsLookup, DnsResolveRecord)
/// through a shared event-loop-aware Task.Run helper (non-blocking).
/// No reflection back to SharpTS.dll.
/// </summary>
public partial class RuntimeEmitter
{
    // These handles are passed only between the construction steps of one emission.
    private readonly record struct DnsDisplay1Construction(
        TypeBuilder Type, FieldBuilder Hostname, FieldBuilder Method,
        ConstructorBuilder Constructor, MethodBuilder Invoke);

    private readonly record struct DnsDisplay2Construction(
        TypeBuilder Type, FieldBuilder Arg0, FieldBuilder Arg1, FieldBuilder Method,
        ConstructorBuilder Constructor, MethodBuilder Invoke);

    private void EmitDnsPromisesMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit display classes for closures
        var display1 = EmitDnsDisplayClass1(typeBuilder.Module as ModuleBuilder ?? throw new Exception("need ModuleBuilder"));
        var display2 = EmitDnsDisplayClass2(typeBuilder.Module as ModuleBuilder ?? throw new Exception("need ModuleBuilder"));
        var runAsync = EmitDnsAsyncRunner(typeBuilder, runtime.EventLoop);

        // Single-arg record type resolvers: hostname → DnsResolveRecord(hostname, rrtype)
        var rrtypes = new (string MethodName, string Rrtype)[]
        {
            ("DnsPromisesResolve4", "A"),
            ("DnsPromisesResolve6", "AAAA"),
            ("DnsPromisesResolveMx", "MX"),
            ("DnsPromisesResolveTxt", "TXT"),
            ("DnsPromisesResolveSrv", "SRV"),
            ("DnsPromisesResolveCname", "CNAME"),
            ("DnsPromisesResolveNs", "NS"),
            ("DnsPromisesResolveSoa", "SOA"),
            ("DnsPromisesResolvePtr", "PTR"),
            ("DnsPromisesResolveCaa", "CAA"),
            ("DnsPromisesResolveNaptr", "NAPTR"),
        };

        foreach (var (methodName, rrtype) in rrtypes)
        {
            var syncHelper = EmitDnsSyncHelper1(typeBuilder, methodName + "_Sync", il =>
            {
                il.Emit(OpCodes.Ldarg_0); // hostname
                il.Emit(OpCodes.Ldstr, rrtype);
                il.Emit(OpCodes.Call, runtime.RequireDns().ResolveRecord);
            });
            EmitDnsAsyncWrapper1(typeBuilder, runtime.RequireDns(), runtime.RequirePromise(), display1, runAsync, methodName, syncHelper);
        }

        // lookup(hostname, options)
        var lookupSync = EmitDnsSyncHelper2(typeBuilder, "DnsPromisesLookup_Sync", il =>
        {
            il.Emit(OpCodes.Ldarg_0); // hostname
            il.Emit(OpCodes.Ldarg_1); // options
            il.Emit(OpCodes.Call, runtime.RequireDns().Lookup);
        });
        EmitDnsAsyncWrapper2(typeBuilder, runtime.RequireDns(), runtime.RequirePromise(), display2, runAsync, "DnsPromisesLookup", lookupSync);

        // lookupService(address, port)
        var lookupServiceSync = EmitDnsSyncHelper2(typeBuilder, "DnsPromisesLookupService_Sync", il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.RequireDns().LookupService);
        });
        EmitDnsAsyncWrapper2(typeBuilder, runtime.RequireDns(), runtime.RequirePromise(), display2, runAsync, "DnsPromisesLookupService", lookupServiceSync);

        // resolve(hostname, rrtype)
        var resolveSync = EmitDnsSyncHelper2(typeBuilder, "DnsPromisesResolve_Sync", il =>
        {
            il.Emit(OpCodes.Ldarg_0); // hostname
            il.Emit(OpCodes.Ldarg_1); // rrtype (already defaulted in wrapper)
            il.Emit(OpCodes.Call, runtime.RequireDns().ResolveRecord);
        });
        EmitDnsAsyncWrapper2(typeBuilder, runtime.RequireDns(), runtime.RequirePromise(), display2, runAsync, "DnsPromisesResolve", resolveSync);

        // reverse(ip)
        var reverseSync = EmitDnsSyncHelper1(typeBuilder, "DnsPromisesReverse_Sync", il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [typeof(string)])!);
            var addrLocal = il.DeclareLocal(typeof(IPAddress));
            il.Emit(OpCodes.Stloc, addrLocal);

            il.Emit(OpCodes.Ldloc, addrLocal);
            il.Emit(OpCodes.Call, typeof(Dns).GetMethod("GetHostEntry", [typeof(IPAddress)])!);
            var entryLocal = il.DeclareLocal(typeof(IPHostEntry));
            il.Emit(OpCodes.Stloc, entryLocal);

            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Callvirt, typeof(IPHostEntry).GetProperty("HostName")!.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);
        });
        EmitDnsAsyncWrapper1(typeBuilder, runtime.RequireDns(), runtime.RequirePromise(), display1, runAsync, "DnsPromisesReverse", reverseSync);

        // Resolver queries use the same event-loop-aware Task.Run path. The sync
        // target late-binds to the shared DnsResolverInstance state in SharpTS.dll;
        // its single object[] request carries state/method/identifier/rrtype.
        EmitDnsAsyncWrapper1(typeBuilder, runtime.RequireDns(), runtime.RequirePromise(), display1, runAsync, "DnsResolverResolveAsync", runtime.RequireDns().ResolverResolve);

        // Namespace getter for dns.promises sub-property
        EmitDnsGetPromisesNamespace(typeBuilder, runtime.RequireDns(), runtime.FunctionConstruction.Constructor,
            runtime.ObjectStorage.Constructor);
    }

    /// <summary>
    /// Emits a 1-arg display class: $DnsDisplay1 { object _hostname; MethodInfo _method; object Invoke() }
    /// The Invoke method calls _method.Invoke(null, [_hostname]).
    /// </summary>
    private DnsDisplay1Construction EmitDnsDisplayClass1(ModuleBuilder moduleBuilder)
    {
        var type = moduleBuilder.DefineType(
            "$DnsDisplay1",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        var hostname = type.DefineField("_hostname", _types.Object, FieldAttributes.Public);
        var method = type.DefineField("_method", typeof(MethodInfo), FieldAttributes.Public);

        var constructor = type.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        {
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);
        }

        // Invoke() → calls _method.Invoke(null, new object[] { _hostname }),
        // preserving the sync helper's original exception as the Task fault.
        var invoke = type.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        {
            var il = invoke.GetILGenerator();
            var resultLocal = il.DeclareLocal(_types.Object);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, method);
            il.Emit(OpCodes.Ldnull); // target (static)
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, hostname);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
            il.Emit(OpCodes.Stloc, resultLocal);
            EmitDnsRethrowInnerException(il);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        type.CreateType();
        return new(type, hostname, method, constructor, invoke);
    }

    /// <summary>
    /// Emits a 2-arg display class: $DnsDisplay2 { object _arg0, _arg1; MethodInfo _method; object Invoke() }
    /// </summary>
    private DnsDisplay2Construction EmitDnsDisplayClass2(ModuleBuilder moduleBuilder)
    {
        var type = moduleBuilder.DefineType(
            "$DnsDisplay2",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        var arg0 = type.DefineField("_arg0", _types.Object, FieldAttributes.Public);
        var arg1 = type.DefineField("_arg1", _types.Object, FieldAttributes.Public);
        var method = type.DefineField("_method", typeof(MethodInfo), FieldAttributes.Public);

        var constructor = type.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        {
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);
        }

        var invoke = type.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        {
            var il = invoke.GetILGenerator();
            var resultLocal = il.DeclareLocal(_types.Object);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, method);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, arg0);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, arg1);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
            il.Emit(OpCodes.Stloc, resultLocal);
            EmitDnsRethrowInnerException(il);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        type.CreateType();
        return new(type, arg0, arg1, method, constructor, invoke);
    }

    /// <summary>
    /// Emits a catch block for reflection-based DNS workers that rethrows the
    /// helper's exception instead of exposing TargetInvocationException.
    /// </summary>
    private void EmitDnsRethrowInnerException(ILGenerator il)
    {
        il.BeginCatchBlock(typeof(TargetInvocationException));
        var exceptionLocal = il.DeclareLocal(typeof(TargetInvocationException));
        var hasInner = il.DefineLabel();
        il.Emit(OpCodes.Stloc, exceptionLocal);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("InnerException")!.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasInner);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.MarkLabel(hasInner);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits the shared DNS promise runner. Each worker owns one event-loop ref.
    /// Its facade task is settled on the loop thread before that ref is released,
    /// ensuring guest await continuations become visible while the loop is live.
    /// </summary>
    private MethodBuilder EmitDnsAsyncRunner(TypeBuilder typeBuilder, EmittedEventLoopRuntime eventLoop)
    {
        var moduleBuilder = typeBuilder.Module as ModuleBuilder ?? throw new Exception("need ModuleBuilder");
        var completionType = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$DnsAsyncCompletion",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);
        var taskField = completionType.DefineField("_task", _types.TaskOfObject, FieldAttributes.Private);
        var tcsField = completionType.DefineField(
            "_completion", _types.TaskCompletionSourceOfObject, FieldAttributes.Private);

        var completionConstructor = completionType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.TaskCompletionSourceOfObject]);
        {
            var il = completionConstructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, tcsField);
            il.Emit(OpCodes.Ret);
        }

        var complete = completionType.DefineMethod(
            "Complete", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        {
            var il = complete.GetILGenerator();
            var canceled = il.DefineLabel();
            var faulted = il.DefineLabel();
            var settled = il.DefineLabel();

            il.BeginExceptionBlock();

            // Cancellation retains its task state rather than becoming a fault.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, taskField);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCanceled").GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, canceled);

            // Fault with the worker's exception, not Task.Exception's AggregateException.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, taskField);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsFaulted").GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, faulted);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, tcsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, taskField);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.TaskOfObject, "Result").GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.TaskCompletionSourceOfObject, "SetResult", [_types.Object])!);
            il.Emit(OpCodes.Br, settled);

            il.MarkLabel(canceled);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, tcsField);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(
                _types.TaskCompletionSourceOfObject, "SetCanceled"));
            il.Emit(OpCodes.Br, settled);

            il.MarkLabel(faulted);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, tcsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, taskField);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "Exception").GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "InnerException").GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.TaskCompletionSourceOfObject, "SetException", [_types.Exception])!);

            // The finally begins only after the facade task has been settled.
            il.MarkLabel(settled);
            il.BeginFinallyBlock();
            il.Emit(OpCodes.Call, eventLoop.GetInstance);
            il.Emit(OpCodes.Callvirt, eventLoop.Unref);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ret);
        }

        // Pool continuation: retain the terminal worker task, then enqueue the
        // settlement action. It never settles or Unrefs from the pool thread.
        var schedule = completionType.DefineMethod(
            "Schedule", MethodAttributes.Public, _types.Void, [_types.TaskOfObject]);
        {
            var il = schedule.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, taskField);
            il.Emit(OpCodes.Call, eventLoop.GetInstance);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldftn, complete);
            il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Callvirt, eventLoop.Schedule);
            il.Emit(OpCodes.Ret);
        }

        completionType.CreateType();

        var taskRunOpen = typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(method => method.Name == "Run" && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType.IsGenericType
                && method.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<>));
        var taskRun = EmitGenerics.MakeGenericMethod(taskRunOpen, _types.Object);
        var funcType = _types.MakeGenericType(typeof(Func<>), _types.Object);
        var continuationType = _types.MakeGenericType(typeof(Action<>), _types.TaskOfObject);

        var runAsync = typeBuilder.DefineMethod(
            "DnsRunAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [funcType]);
        {
            var il = runAsync.GetILGenerator();
            var tcsLocal = il.DeclareLocal(_types.TaskCompletionSourceOfObject);
            var completionLocal = il.DeclareLocal(completionType);
            var taskLocal = il.DeclareLocal(_types.TaskOfObject);
            var exceptionLocal = il.DeclareLocal(_types.Exception);

            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.TaskCompletionSourceOfObject));
            il.Emit(OpCodes.Stloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Newobj, completionConstructor);
            il.Emit(OpCodes.Stloc, completionLocal);

            il.Emit(OpCodes.Call, eventLoop.GetInstance);
            il.Emit(OpCodes.Callvirt, eventLoop.Ref);

            // Balance the ref if Task.Run or continuation registration throws
            // synchronously. Once registered, the completion closure owns it.
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, taskRun);
            il.Emit(OpCodes.Stloc, taskLocal);
            il.Emit(OpCodes.Ldloc, taskLocal);
            il.Emit(OpCodes.Ldloc, completionLocal);
            il.Emit(OpCodes.Ldftn, schedule);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(
                continuationType, [_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.TaskOfObject,
                "ContinueWith",
                [continuationType, typeof(TaskContinuationOptions)])!);
            il.Emit(OpCodes.Pop);
            il.BeginCatchBlock(_types.Exception);
            il.Emit(OpCodes.Stloc, exceptionLocal);
            il.Emit(OpCodes.Call, eventLoop.GetInstance);
            il.Emit(OpCodes.Callvirt, eventLoop.Unref);
            il.Emit(OpCodes.Ldloc, exceptionLocal);
            il.Emit(OpCodes.Throw);
            il.EndExceptionBlock();

            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(
                _types.TaskCompletionSourceOfObject, "Task").GetGetMethod()!);
            il.Emit(OpCodes.Ret);
        }

        return runAsync;
    }

    /// <summary>
    /// Emits a 1-arg sync helper: static object MethodName(object hostname) { ... }
    /// </summary>
    private MethodBuilder EmitDnsSyncHelper1(TypeBuilder typeBuilder,
        string methodName, Action<ILGenerator> emitBody)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);

        var il = method.GetILGenerator();
        emitBody(il);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits a 2-arg sync helper: static object MethodName(object arg0, object arg1) { ... }
    /// </summary>
    private MethodBuilder EmitDnsSyncHelper2(TypeBuilder typeBuilder,
        string methodName, Action<ILGenerator> emitBody)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);

        var il = method.GetILGenerator();
        emitBody(il);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits 1-arg async wrapper: creates the worker closure, then calls the
    /// shared event-loop-aware runner and WrapTaskAsPromise.
    /// </summary>
    private void EmitDnsAsyncWrapper1(TypeBuilder typeBuilder, EmittedDnsRuntime dns,
        EmittedPromiseRuntime promise, DnsDisplay1Construction display, MethodBuilder runAsync,
        string methodName, MethodBuilder syncHelper)
    {
        var wrapper = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);

        var il = wrapper.GetILGenerator();

        // var dc = new $DnsDisplay1();
        il.Emit(OpCodes.Newobj, display.Constructor);
        var dcLocal = il.DeclareLocal(display.Type);
        il.Emit(OpCodes.Stloc, dcLocal);

        // dc._hostname = arg0;
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, display.Hostname);

        // dc._method = syncHelper (via Ldtoken)
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldtoken, syncHelper);
        il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        il.Emit(OpCodes.Stfld, display.Method);

        // DnsRunAsync(new Func<object?>(dc.Invoke))
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldftn, display.Invoke);
        il.Emit(OpCodes.Newobj, typeof(Func<object?>).GetConstructors()[0]);
        il.Emit(OpCodes.Call, runAsync);

        // WrapTaskAsPromise
        il.Emit(OpCodes.Call, promise.WrapTaskAsPromise);
        il.Emit(OpCodes.Ret);

        dns.RegisterPromiseWrapper(methodName, wrapper);
    }

    /// <summary>
    /// Emits 2-arg async wrapper: packs both arguments into the worker closure,
    /// then uses the same event-loop-aware runner as the 1-arg path.
    /// </summary>
    private void EmitDnsAsyncWrapper2(TypeBuilder typeBuilder, EmittedDnsRuntime dns,
        EmittedPromiseRuntime promise, DnsDisplay2Construction display, MethodBuilder runAsync,
        string methodName, MethodBuilder syncHelper)
    {
        var wrapper = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);

        var il = wrapper.GetILGenerator();

        // Default rrtype to "A" for resolve
        if (methodName == "DnsPromisesResolve")
        {
            var hasRrtypeLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brtrue, hasRrtypeLabel);
            il.Emit(OpCodes.Ldstr, "A");
            il.Emit(OpCodes.Starg, 1);
            il.MarkLabel(hasRrtypeLabel);
        }

        // var dc = new $DnsDisplay2();
        il.Emit(OpCodes.Newobj, display.Constructor);
        var dcLocal = il.DeclareLocal(display.Type);
        il.Emit(OpCodes.Stloc, dcLocal);

        // dc._arg0 = arg0; dc._arg1 = arg1;
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, display.Arg0);
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, display.Arg1);

        // dc._method = syncHelper
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldtoken, syncHelper);
        il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        il.Emit(OpCodes.Stfld, display.Method);

        // DnsRunAsync(new Func<object?>(dc.Invoke))
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldftn, display.Invoke);
        il.Emit(OpCodes.Newobj, typeof(Func<object?>).GetConstructors()[0]);
        il.Emit(OpCodes.Call, runAsync);

        // WrapTaskAsPromise
        il.Emit(OpCodes.Call, promise.WrapTaskAsPromise);
        il.Emit(OpCodes.Ret);

        dns.RegisterPromiseWrapper(methodName, wrapper);
    }

    /// <summary>
    /// Emits DnsGetPromisesNamespace: creates a Dictionary&lt;string, object?&gt; namespace
    /// with TSFunction entries for each dns/promises method.
    /// </summary>
    private void EmitDnsGetPromisesNamespace(TypeBuilder typeBuilder, EmittedDnsRuntime dns,
        ConstructorBuilder functionConstructor, ConstructorBuilder objectConstructor)
    {
        var method = typeBuilder.DefineMethod(
            "DnsGetPromisesNamespace",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        dns.GetPromisesNamespace = method;

        var il = method.GetILGenerator();

        var dictCtor = _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!;
        var addMethod = _types.GetMethod(_types.DictionaryStringObject, "Add", [typeof(string), typeof(object)])!;

        il.Emit(OpCodes.Newobj, dictCtor);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        var methodMap = new (string JsName, string WrapperKey)[]
        {
            ("lookup", "DnsPromisesLookup"),
            ("lookupService", "DnsPromisesLookupService"),
            ("resolve", "DnsPromisesResolve"),
            ("resolve4", "DnsPromisesResolve4"),
            ("resolve6", "DnsPromisesResolve6"),
            ("reverse", "DnsPromisesReverse"),
            ("resolveMx", "DnsPromisesResolveMx"),
            ("resolveTxt", "DnsPromisesResolveTxt"),
            ("resolveSrv", "DnsPromisesResolveSrv"),
            ("resolveCname", "DnsPromisesResolveCname"),
            ("resolveNs", "DnsPromisesResolveNs"),
            ("resolveSoa", "DnsPromisesResolveSoa"),
            ("resolvePtr", "DnsPromisesResolvePtr"),
            ("resolveCaa", "DnsPromisesResolveCaa"),
            ("resolveNaptr", "DnsPromisesResolveNaptr"),
        };

        foreach (var (jsName, wrapperKey) in methodMap)
        {
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, jsName);

            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldtoken, dns.PromisesWrapperMethods[wrapperKey]);
            il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            il.Emit(OpCodes.Newobj, functionConstructor);

            il.Emit(OpCodes.Call, addMethod);
        }

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Newobj, objectConstructor);
        il.Emit(OpCodes.Ret);
    }
}
