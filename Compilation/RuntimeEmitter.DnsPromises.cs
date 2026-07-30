using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits dns/promises support methods into the $Runtime class.
/// Pure IL — calls existing emitted DNS sync methods (DnsLookup, DnsResolveRecord)
/// wrapped in Task.Run via display classes to run on a thread pool thread (non-blocking).
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

    private void EmitDnsPromisesMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.DnsPromisesWrapperMethods = new Dictionary<string, MethodBuilder>();

        // Emit display classes for closures
        EmitDnsDisplayClass1(typeBuilder.Module as ModuleBuilder ?? throw new Exception("need ModuleBuilder"));
        EmitDnsDisplayClass2(typeBuilder.Module as ModuleBuilder ?? throw new Exception("need ModuleBuilder"));

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

        // Invoke() → calls _method.Invoke(null, new object[] { _hostname })
        _dnsDisplay1Invoke = _dnsDisplayClass1.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        {
            var il = _dnsDisplay1Invoke.GetILGenerator();
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
            il.Emit(OpCodes.Ret);
        }

        _dnsDisplayClass2.CreateType();
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
    /// Emits 1-arg async wrapper: creates display class, calls Task.Run(() => syncHelper(hostname)) → WrapTaskAsPromise.
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

        // Task.Run<object?>(new Func<object?>(dc.Invoke))
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldftn, _dnsDisplay1Invoke);
        il.Emit(OpCodes.Newobj, typeof(Func<object?>).GetConstructors()[0]);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("Run", 1, [_types.MakeGenericType(typeof(Func<>), Type.MakeGenericMethodParameter(0))])!, typeof(object)));

        // WrapTaskAsPromise
        il.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);
        il.Emit(OpCodes.Ret);

        runtime.DnsPromisesWrapperMethods[methodName] = wrapper;
    }

    /// <summary>
    /// Emits 2-arg async wrapper: similar but packs 2 args into display class.
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

        // Task.Run<object?>(new Func<object?>(dc.Invoke))
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldftn, _dnsDisplay2Invoke);
        il.Emit(OpCodes.Newobj, typeof(Func<object?>).GetConstructors()[0]);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("Run", 1, [_types.MakeGenericType(typeof(Func<>), Type.MakeGenericMethodParameter(0))])!, typeof(object)));

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
