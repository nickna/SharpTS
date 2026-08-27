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
    // Display class types for DNS promise closures
    private TypeBuilder _dnsDisplayClass1 = null!; // 1-arg: hostname field + method field + Invoke
    private FieldBuilder _dnsDisplay1Hostname = null!;
    private FieldBuilder _dnsDisplay1Method = null!;
    private ConstructorBuilder _dnsDisplay1Ctor = null!;
    private MethodBuilder _dnsDisplay1Invoke = null!;

    private TypeBuilder _dnsDisplayClass2 = null!; // 2-arg: arg0, arg1, method fields + Invoke
    private FieldBuilder _dnsDisplay2Arg0 = null!;
    private FieldBuilder _dnsDisplay2Arg1 = null!;
    private FieldBuilder _dnsDisplay2Method = null!;
    private ConstructorBuilder _dnsDisplay2Ctor = null!;
    private MethodBuilder _dnsDisplay2Invoke = null!;

    // Shared promise runner infrastructure. The completion closure transfers the
    // pool task's terminal state to a facade task on the event-loop thread.
    private ConstructorBuilder _dnsAsyncCompletionCtor = null!;
    private MethodBuilder _dnsAsyncCompletionSchedule = null!;
    private MethodBuilder _dnsRunAsync = null!;

    private void EmitDnsPromisesMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.DnsPromisesWrapperMethods = new Dictionary<string, MethodBuilder>();

        // Emit display classes for closures
        EmitDnsDisplayClass1(typeBuilder.Module as ModuleBuilder ?? throw new Exception("need ModuleBuilder"));
        EmitDnsDisplayClass2(typeBuilder.Module as ModuleBuilder ?? throw new Exception("need ModuleBuilder"));
        EmitDnsAsyncRunner(typeBuilder, runtime);

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
            var syncHelper = EmitDnsSyncHelper1(typeBuilder, runtime, methodName + "_Sync", il =>
            {
                il.Emit(OpCodes.Ldarg_0); // hostname
                il.Emit(OpCodes.Ldstr, rrtype);
                il.Emit(OpCodes.Call, runtime.DnsResolveRecord);
            });
            EmitDnsAsyncWrapper1(typeBuilder, runtime, methodName, syncHelper);
        }

        // lookup(hostname, options)
        var lookupSync = EmitDnsSyncHelper2(typeBuilder, runtime, "DnsPromisesLookup_Sync", il =>
        {
            il.Emit(OpCodes.Ldarg_0); // hostname
            il.Emit(OpCodes.Ldarg_1); // options
            il.Emit(OpCodes.Call, runtime.DnsLookup);
        });
        EmitDnsAsyncWrapper2(typeBuilder, runtime, "DnsPromisesLookup", lookupSync);

        // lookupService(address, port)
        var lookupServiceSync = EmitDnsSyncHelper2(typeBuilder, runtime, "DnsPromisesLookupService_Sync", il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsLookupService);
        });
        EmitDnsAsyncWrapper2(typeBuilder, runtime, "DnsPromisesLookupService", lookupServiceSync);

        // resolve(hostname, rrtype)
        var resolveSync = EmitDnsSyncHelper2(typeBuilder, runtime, "DnsPromisesResolve_Sync", il =>
        {
            il.Emit(OpCodes.Ldarg_0); // hostname
            il.Emit(OpCodes.Ldarg_1); // rrtype (already defaulted in wrapper)
            il.Emit(OpCodes.Call, runtime.DnsResolveRecord);
        });
        EmitDnsAsyncWrapper2(typeBuilder, runtime, "DnsPromisesResolve", resolveSync);

        // reverse(ip)
        var reverseSync = EmitDnsSyncHelper1(typeBuilder, runtime, "DnsPromisesReverse_Sync", il =>
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
        EmitDnsAsyncWrapper1(typeBuilder, runtime, "DnsPromisesReverse", reverseSync);

        // Resolver queries use the same event-loop-aware Task.Run path. The sync
        // target late-binds to the shared DnsResolverInstance state in SharpTS.dll;
        // its single object[] request carries state/method/identifier/rrtype.
        EmitDnsAsyncWrapper1(typeBuilder, runtime, "DnsResolverResolveAsync", runtime.DnsResolverResolve);

        // Namespace getter for dns.promises sub-property
        EmitDnsGetPromisesNamespace(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits a 1-arg display class: $DnsDisplay1 { object _hostname; MethodInfo _method; object Invoke() }
    /// The Invoke method calls _method.Invoke(null, [_hostname]).
    /// </summary>
    private void EmitDnsDisplayClass1(ModuleBuilder moduleBuilder)
    {
        _dnsDisplayClass1 = moduleBuilder.DefineType(
            "$DnsDisplay1",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        _dnsDisplay1Hostname = _dnsDisplayClass1.DefineField("_hostname", _types.Object, FieldAttributes.Public);
        _dnsDisplay1Method = _dnsDisplayClass1.DefineField("_method", typeof(MethodInfo), FieldAttributes.Public);

        _dnsDisplay1Ctor = _dnsDisplayClass1.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        {
            var il = _dnsDisplay1Ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);
        }

        // Invoke() → calls _method.Invoke(null, new object[] { _hostname }),
        // preserving the sync helper's original exception as the Task fault.
        _dnsDisplay1Invoke = _dnsDisplayClass1.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        {
            var il = _dnsDisplay1Invoke.GetILGenerator();
            var resultLocal = il.DeclareLocal(_types.Object);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _dnsDisplay1Method);
            il.Emit(OpCodes.Ldnull); // target (static)
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _dnsDisplay1Hostname);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
            il.Emit(OpCodes.Stloc, resultLocal);
            EmitDnsRethrowInnerException(il);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        _dnsDisplayClass1.CreateType();
    }

    /// <summary>
    /// Emits a 2-arg display class: $DnsDisplay2 { object _arg0, _arg1; MethodInfo _method; object Invoke() }
    /// </summary>
    private void EmitDnsDisplayClass2(ModuleBuilder moduleBuilder)
    {
        _dnsDisplayClass2 = moduleBuilder.DefineType(
            "$DnsDisplay2",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        _dnsDisplay2Arg0 = _dnsDisplayClass2.DefineField("_arg0", _types.Object, FieldAttributes.Public);
        _dnsDisplay2Arg1 = _dnsDisplayClass2.DefineField("_arg1", _types.Object, FieldAttributes.Public);
        _dnsDisplay2Method = _dnsDisplayClass2.DefineField("_method", typeof(MethodInfo), FieldAttributes.Public);

        _dnsDisplay2Ctor = _dnsDisplayClass2.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        {
            var il = _dnsDisplay2Ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);
        }

        _dnsDisplay2Invoke = _dnsDisplayClass2.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        {
            var il = _dnsDisplay2Invoke.GetILGenerator();
            var resultLocal = il.DeclareLocal(_types.Object);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _dnsDisplay2Method);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _dnsDisplay2Arg0);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _dnsDisplay2Arg1);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
            il.Emit(OpCodes.Stloc, resultLocal);
            EmitDnsRethrowInnerException(il);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        _dnsDisplayClass2.CreateType();
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
    private void EmitDnsAsyncRunner(TypeBuilder typeBuilder, EmittedRuntime runtime)
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

        _dnsAsyncCompletionCtor = completionType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.TaskCompletionSourceOfObject]);
        {
            var il = _dnsAsyncCompletionCtor.GetILGenerator();
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
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Callvirt, runtime.EventLoopUnref);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ret);
        }

        // Pool continuation: retain the terminal worker task, then enqueue the
        // settlement action. It never settles or Unrefs from the pool thread.
        _dnsAsyncCompletionSchedule = completionType.DefineMethod(
            "Schedule", MethodAttributes.Public, _types.Void, [_types.TaskOfObject]);
        {
            var il = _dnsAsyncCompletionSchedule.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, taskField);
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldftn, complete);
            il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);
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

        _dnsRunAsync = typeBuilder.DefineMethod(
            "DnsRunAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [funcType]);
        {
            var il = _dnsRunAsync.GetILGenerator();
            var tcsLocal = il.DeclareLocal(_types.TaskCompletionSourceOfObject);
            var completionLocal = il.DeclareLocal(completionType);
            var taskLocal = il.DeclareLocal(_types.TaskOfObject);
            var exceptionLocal = il.DeclareLocal(_types.Exception);

            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.TaskCompletionSourceOfObject));
            il.Emit(OpCodes.Stloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Newobj, _dnsAsyncCompletionCtor);
            il.Emit(OpCodes.Stloc, completionLocal);

            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Callvirt, runtime.EventLoopRef);

            // Balance the ref if Task.Run or continuation registration throws
            // synchronously. Once registered, the completion closure owns it.
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, taskRun);
            il.Emit(OpCodes.Stloc, taskLocal);
            il.Emit(OpCodes.Ldloc, taskLocal);
            il.Emit(OpCodes.Ldloc, completionLocal);
            il.Emit(OpCodes.Ldftn, _dnsAsyncCompletionSchedule);
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
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Callvirt, runtime.EventLoopUnref);
            il.Emit(OpCodes.Ldloc, exceptionLocal);
            il.Emit(OpCodes.Throw);
            il.EndExceptionBlock();

            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(
                _types.TaskCompletionSourceOfObject, "Task").GetGetMethod()!);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits a 1-arg sync helper: static object MethodName(object hostname) { ... }
    /// </summary>
    private MethodBuilder EmitDnsSyncHelper1(TypeBuilder typeBuilder, EmittedRuntime runtime,
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
    private MethodBuilder EmitDnsSyncHelper2(TypeBuilder typeBuilder, EmittedRuntime runtime,
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
    private void EmitDnsAsyncWrapper1(TypeBuilder typeBuilder, EmittedRuntime runtime,
        string methodName, MethodBuilder syncHelper)
    {
        var wrapper = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);

        var il = wrapper.GetILGenerator();

        // var dc = new $DnsDisplay1();
        il.Emit(OpCodes.Newobj, _dnsDisplay1Ctor);
        var dcLocal = il.DeclareLocal(_dnsDisplayClass1);
        il.Emit(OpCodes.Stloc, dcLocal);

        // dc._hostname = arg0;
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, _dnsDisplay1Hostname);

        // dc._method = syncHelper (via Ldtoken)
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldtoken, syncHelper);
        il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        il.Emit(OpCodes.Stfld, _dnsDisplay1Method);

        // DnsRunAsync(new Func<object?>(dc.Invoke))
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldftn, _dnsDisplay1Invoke);
        il.Emit(OpCodes.Newobj, typeof(Func<object?>).GetConstructors()[0]);
        il.Emit(OpCodes.Call, _dnsRunAsync);

        // WrapTaskAsPromise
        il.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);
        il.Emit(OpCodes.Ret);

        runtime.DnsPromisesWrapperMethods[methodName] = wrapper;
    }

    /// <summary>
    /// Emits 2-arg async wrapper: packs both arguments into the worker closure,
    /// then uses the same event-loop-aware runner as the 1-arg path.
    /// </summary>
    private void EmitDnsAsyncWrapper2(TypeBuilder typeBuilder, EmittedRuntime runtime,
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
        il.Emit(OpCodes.Newobj, _dnsDisplay2Ctor);
        var dcLocal = il.DeclareLocal(_dnsDisplayClass2);
        il.Emit(OpCodes.Stloc, dcLocal);

        // dc._arg0 = arg0; dc._arg1 = arg1;
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, _dnsDisplay2Arg0);
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _dnsDisplay2Arg1);

        // dc._method = syncHelper
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldtoken, syncHelper);
        il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        il.Emit(OpCodes.Stfld, _dnsDisplay2Method);

        // DnsRunAsync(new Func<object?>(dc.Invoke))
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldftn, _dnsDisplay2Invoke);
        il.Emit(OpCodes.Newobj, typeof(Func<object?>).GetConstructors()[0]);
        il.Emit(OpCodes.Call, _dnsRunAsync);

        // WrapTaskAsPromise
        il.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);
        il.Emit(OpCodes.Ret);

        runtime.DnsPromisesWrapperMethods[methodName] = wrapper;
    }

    /// <summary>
    /// Emits DnsGetPromisesNamespace: creates a Dictionary&lt;string, object?&gt; namespace
    /// with TSFunction entries for each dns/promises method.
    /// </summary>
    private void EmitDnsGetPromisesNamespace(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsGetPromisesNamespace",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        runtime.DnsGetPromisesNamespace = method;

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
            il.Emit(OpCodes.Ldtoken, runtime.DnsPromisesWrapperMethods[wrapperKey]);
            il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);

            il.Emit(OpCodes.Call, addMethod);
        }

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Ret);
    }
}
