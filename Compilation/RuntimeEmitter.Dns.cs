using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// DNS module methods for standalone assemblies.
/// Uses pure BCL DNS wire protocol — no external dependencies.
/// </summary>
public partial class RuntimeEmitter
{
    // Emitted static holding the default result order (#1072); null = "verbatim".
    // Kept in the output assembly so dns.lookup ordering works standalone; the
    // setter also best-effort syncs SharpTS.dll's DnsConfig (late-bound) so the
    // Resolver/promises paths observe it.
    private FieldBuilder _dnsResultOrderField = null!;

    private void EmitDnsModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        _dnsResultOrderField = typeBuilder.DefineField("_dnsResultOrder", _types.String,
            FieldAttributes.Private | FieldAttributes.Static);
        EmitDnsResultOrderAccessors(typeBuilder, runtime);
        EmitDnsLookup(typeBuilder, runtime);
        EmitDnsLookupService(typeBuilder, runtime);
        EmitDnsGetLookup(typeBuilder, runtime);
        EmitDnsGetLookupService(typeBuilder, runtime);

        // DNS wire protocol helpers (emitted into output assembly, BCL-only)
        EmitDnsGetTimeoutMsMethod(typeBuilder, runtime);
        EmitDnsParseServerEndpointMethod(typeBuilder, runtime);
        EmitDnsReadUInt16(typeBuilder, runtime);
        EmitDnsReadUInt32(typeBuilder, runtime);
        EmitDnsReadCharString(typeBuilder, runtime);
        EmitDnsReadExact(typeBuilder, runtime);
        EmitDnsEncodeName(typeBuilder, runtime);
        EmitDnsReadNameMethod(typeBuilder, runtime);
        EmitDnsSkipNameMethod(typeBuilder, runtime);
        EmitDnsGetSystemDnsMethod(typeBuilder, runtime);
        EmitDnsBuildQueryMethod(typeBuilder, runtime);
        EmitDnsSendViaTcpMethod(typeBuilder, runtime);
        EmitDnsSendReceiveMethod(typeBuilder, runtime);
        EmitDnsParseRecordMethod(typeBuilder, runtime);
        EmitDnsParseResponseMethod(typeBuilder, runtime);

        // DNS record resolution (MX, TXT, SRV, CNAME, NS, SOA, PTR, CAA, NAPTR)
        EmitDnsConvertList(typeBuilder, runtime);
        EmitDnsFindChaseTarget(typeBuilder, runtime);
        EmitDnsDoQuery(typeBuilder, runtime);
        EmitDnsResolveRecord(typeBuilder, runtime);

        // Async (callback-based) DNS resolution wrappers
        EmitDnsAsyncResolveWrappers(typeBuilder, runtime);

        // Async wrappers for DNS record types
        EmitDnsRecordTypeWrappers(typeBuilder, runtime);

        // DNS Resolver factory
        EmitDnsResolverFactory(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits DnsGetDefaultResultOrder / DnsSetDefaultResultOrder (#1072).
    /// The setter validates ('ipv4first'|'ipv6first'|'verbatim'), stores the emitted
    /// static, and best-effort late-binds RuntimeTypes.DnsSetDefaultResultOrder so
    /// SharpTS.dll-side dns paths (Resolver, promises) stay in sync. dns already
    /// records the "dns module" soft-dependency; when SharpTS.dll is absent the
    /// sync silently no-ops and the standalone lookup path still honors the order.
    /// </summary>
    private void EmitDnsResultOrderAccessors(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // getDefaultResultOrder(): return _dnsResultOrder ?? "verbatim"
        {
            var method = typeBuilder.DefineMethod(
                "DnsGetDefaultResultOrder",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                Type.EmptyTypes
            );
            runtime.DnsGetDefaultResultOrder = method;
            runtime.RegisterBuiltInModuleMethod("dns", "getDefaultResultOrder", method);
            runtime.RegisterBuiltInModuleMethod("dns/promises", "getDefaultResultOrder", method);

            var il = method.GetILGenerator();
            var haveValue = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, _dnsResultOrderField);
            il.Emit(OpCodes.Brtrue, haveValue);
            il.Emit(OpCodes.Ldstr, "verbatim");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(haveValue);
            il.Emit(OpCodes.Ldsfld, _dnsResultOrderField);
            il.Emit(OpCodes.Ret);
        }

        // setDefaultResultOrder(order)
        {
            var method = typeBuilder.DefineMethod(
                "DnsSetDefaultResultOrder",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object]
            );
            runtime.DnsSetDefaultResultOrder = method;
            runtime.RegisterBuiltInModuleMethod("dns", "setDefaultResultOrder", method);
            runtime.RegisterBuiltInModuleMethod("dns/promises", "setDefaultResultOrder", method);

            var il = method.GetILGenerator();
            var strLocal = il.DeclareLocal(_types.String);
            var valid = il.DefineLabel();
            var invalid = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc, strLocal);
            il.Emit(OpCodes.Brfalse, invalid);

            foreach (var allowed in new[] { "ipv4first", "ipv6first", "verbatim" })
            {
                il.Emit(OpCodes.Ldloc, strLocal);
                il.Emit(OpCodes.Ldstr, allowed);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String, _types.String])!);
                il.Emit(OpCodes.Brtrue, valid);
            }

            il.MarkLabel(invalid);
            GuestErrorEmitter.ThrowTypeError(il, runtime, "The argument 'order' must be one of: 'ipv4first', 'ipv6first', 'verbatim'");

            il.MarkLabel(valid);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Stsfld, _dnsResultOrderField);

            // try { Type.GetType("SharpTS.Compilation.RuntimeTypes, SharpTS")
            //           ?.GetMethod("DnsSetDefaultResultOrder")?.Invoke(null, [order]) } catch { }
            il.BeginExceptionBlock();
            {
                var typeLocal = il.DeclareLocal(typeof(Type));
                var methodLocal = il.DeclareLocal(typeof(MethodInfo));
                var syncDone = il.DefineLabel();

                il.Emit(OpCodes.Ldstr, "SharpTS.Compilation.RuntimeTypes, SharpTS");
                il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetType", [_types.String])!);
                il.Emit(OpCodes.Stloc, typeLocal);
                il.Emit(OpCodes.Ldloc, typeLocal);
                il.Emit(OpCodes.Brfalse, syncDone);

                il.Emit(OpCodes.Ldloc, typeLocal);
                il.Emit(OpCodes.Ldstr, "DnsSetDefaultResultOrder");
                il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [_types.String])!);
                il.Emit(OpCodes.Stloc, methodLocal);
                il.Emit(OpCodes.Ldloc, methodLocal);
                il.Emit(OpCodes.Brfalse, syncDone);

                il.Emit(OpCodes.Ldloc, methodLocal);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldloc, strLocal);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [_types.Object, typeof(object[])])!);
                il.Emit(OpCodes.Pop);

                il.MarkLabel(syncDone);
            }
            il.BeginCatchBlock(_types.Exception);
            il.Emit(OpCodes.Pop);
            il.EndExceptionBlock();

            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits DnsLookup: resolves a hostname to an IP address.
    /// Signature: object DnsLookup(object hostname, object options)
    /// Returns a Dictionary with { address: string, family: number }.
    /// Options can be a number (4 or 6) to request a specific address family.
    /// </summary>
    private void EmitDnsLookup(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsLookup",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.DnsLookup = method;

        var il = method.GetILGenerator();

        // Local variables
        var hostnameLocal = il.DeclareLocal(_types.String);          // 0
        var hostEntryLocal = il.DeclareLocal(typeof(IPHostEntry));   // 1
        var resultLocal = il.DeclareLocal(_types.Object);            // 2
        var requestedFamilyLocal = il.DeclareLocal(_types.Int32);    // 3: 0 = any, 4 = IPv4, 6 = IPv6
        var selectedAddressLocal = il.DeclareLocal(typeof(IPAddress)); // 4
        var addressListLocal = il.DeclareLocal(typeof(IPAddress[])); // 5
        var indexLocal = il.DeclareLocal(_types.Int32);              // 6
        var currentAddressLocal = il.DeclareLocal(typeof(IPAddress)); // 7
        var firstAnyLocal = il.DeclareLocal(typeof(IPAddress));      // first address regardless of family
        var softPreferenceLocal = il.DeclareLocal(_types.Boolean);   // family derived from result order, not options

        // Labels
        var parseOptionsLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();
        var checkOptionsIsDoubleLabel = il.DefineLabel();
        var optionsParsedLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopBodyLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var checkFamilyLabel = il.DefineLabel();
        var addressMatchedLabel = il.DefineLabel();
        var loopContinueLabel = il.DefineLabel();
        var foundAddressLabel = il.DefineLabel();

        // Extract hostname from arg0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, hostnameLocal);
        il.Emit(OpCodes.Brtrue, checkOptionsIsDoubleLabel);

        // Throw error if hostname is not a string
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.lookup requires a hostname string");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Parse options - check if it's a double (family number)
        il.MarkLabel(checkOptionsIsDoubleLabel);
        il.Emit(OpCodes.Ldc_I4_0);  // default: any family
        il.Emit(OpCodes.Stloc, requestedFamilyLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, parseOptionsLabel);  // null options = any family

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, parseOptionsLabel);  // not a double = any family

        // It's a double - get the value
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, requestedFamilyLocal);

        il.MarkLabel(parseOptionsLabel);

        // Result order (#1072): with no explicit family, derive a SOFT family
        // preference from _dnsResultOrder ('ipv4first'/'ipv6first'); 'verbatim'
        // (or unset) keeps first-answer selection.
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, softPreferenceLocal);

            var orderDone = il.DefineLabel();
            var tryV6First = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, requestedFamilyLocal);
            il.Emit(OpCodes.Brtrue, orderDone); // explicit family wins
            il.Emit(OpCodes.Ldsfld, _dnsResultOrderField);
            il.Emit(OpCodes.Brfalse, orderDone); // unset = verbatim

            il.Emit(OpCodes.Ldsfld, _dnsResultOrderField);
            il.Emit(OpCodes.Ldstr, "ipv4first");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String, _types.String])!);
            il.Emit(OpCodes.Brfalse, tryV6First);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Stloc, requestedFamilyLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, softPreferenceLocal);
            il.Emit(OpCodes.Br, orderDone);

            il.MarkLabel(tryV6First);
            il.Emit(OpCodes.Ldsfld, _dnsResultOrderField);
            il.Emit(OpCodes.Ldstr, "ipv6first");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String, _types.String])!);
            il.Emit(OpCodes.Brfalse, orderDone);
            il.Emit(OpCodes.Ldc_I4_6);
            il.Emit(OpCodes.Stloc, requestedFamilyLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, softPreferenceLocal);

            il.MarkLabel(orderDone);
        }

        // Try-catch block for Dns.GetHostEntry
        il.BeginExceptionBlock();

        // var hostEntry = Dns.GetHostEntry(hostname);
        il.Emit(OpCodes.Ldloc, hostnameLocal);
        il.Emit(OpCodes.Call, typeof(Dns).GetMethod("GetHostEntry", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, hostEntryLocal);

        // var addressList = hostEntry.AddressList;
        il.Emit(OpCodes.Ldloc, hostEntryLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPHostEntry).GetProperty("AddressList")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, addressListLocal);

        // selectedAddress = null; firstAny = null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, selectedAddressLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, firstAnyLocal);

        // index = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop: for (int i = 0; i < addressList.Length; i++)
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, addressListLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEndLabel);  // if index >= length, exit loop

        // currentAddress = addressList[index]
        il.Emit(OpCodes.Ldloc, addressListLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, currentAddressLocal);

        // if (firstAny == null) firstAny = currentAddress — order-preference fallback
        {
            var haveFirst = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, firstAnyLocal);
            il.Emit(OpCodes.Brtrue, haveFirst);
            il.Emit(OpCodes.Ldloc, currentAddressLocal);
            il.Emit(OpCodes.Stloc, firstAnyLocal);
            il.MarkLabel(haveFirst);
        }

        // if (requestedFamily == 0) { selectedAddress = currentAddress; break; }
        il.Emit(OpCodes.Ldloc, requestedFamilyLocal);
        il.Emit(OpCodes.Brtrue, checkFamilyLabel);
        il.Emit(OpCodes.Ldloc, currentAddressLocal);
        il.Emit(OpCodes.Stloc, selectedAddressLocal);
        il.Emit(OpCodes.Br, loopEndLabel);

        // Check if address matches requested family
        il.MarkLabel(checkFamilyLabel);

        // if (requestedFamily == 4 && currentAddress.AddressFamily == InterNetwork)
        il.Emit(OpCodes.Ldloc, requestedFamilyLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Bne_Un, loopContinueLabel);  // skip to IPv6 check

        il.Emit(OpCodes.Ldloc, currentAddressLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetwork);
        il.Emit(OpCodes.Bne_Un, loopContinueLabel);
        il.Emit(OpCodes.Ldloc, currentAddressLocal);
        il.Emit(OpCodes.Stloc, selectedAddressLocal);
        il.Emit(OpCodes.Br, loopEndLabel);

        // if (requestedFamily == 6 && currentAddress.AddressFamily == InterNetworkV6)
        il.MarkLabel(loopContinueLabel);
        il.Emit(OpCodes.Ldloc, requestedFamilyLocal);
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Bne_Un, addressMatchedLabel);  // not 6, skip

        il.Emit(OpCodes.Ldloc, currentAddressLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetworkV6);
        il.Emit(OpCodes.Bne_Un, addressMatchedLabel);
        il.Emit(OpCodes.Ldloc, currentAddressLocal);
        il.Emit(OpCodes.Stloc, selectedAddressLocal);
        il.Emit(OpCodes.Br, loopEndLabel);

        // index++; continue loop
        il.MarkLabel(addressMatchedLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // Soft order preference (#1072): no address of the preferred family →
        // fall back to the first address instead of failing.
        {
            var noFallback = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, selectedAddressLocal);
            il.Emit(OpCodes.Brtrue, noFallback);
            il.Emit(OpCodes.Ldloc, softPreferenceLocal);
            il.Emit(OpCodes.Brfalse, noFallback);
            il.Emit(OpCodes.Ldloc, firstAnyLocal);
            il.Emit(OpCodes.Stloc, selectedAddressLocal);
            il.MarkLabel(noFallback);
        }

        // if (selectedAddress == null) throw ENOTFOUND
        il.Emit(OpCodes.Ldloc, selectedAddressLocal);
        il.Emit(OpCodes.Brtrue, foundAddressLabel);

        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.lookup ENOTFOUND ");
        il.Emit(OpCodes.Ldloc, hostnameLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(foundAddressLabel);

        // Create result object { address: string, family: number }
        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var addMethod = _types.GetMethod(dictType, "Add", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        // address = selectedAddress.ToString()
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "address");
        il.Emit(OpCodes.Ldloc, selectedAddressLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, addMethod);

        // family = selectedAddress.AddressFamily == InterNetwork ? 4.0 : 6.0
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "family");
        il.Emit(OpCodes.Ldloc, selectedAddressLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetwork);
        var isIpv6Label = il.DefineLabel();
        var familyDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, isIpv6Label);
        il.Emit(OpCodes.Ldc_R8, 4.0);
        il.Emit(OpCodes.Br, familyDoneLabel);
        il.MarkLabel(isIpv6Label);
        il.Emit(OpCodes.Ldc_R8, 6.0);
        il.MarkLabel(familyDoneLabel);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, addMethod);

        // Wrap in TSObject and store in result
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Leave try block
        il.Emit(OpCodes.Leave, returnLabel);

        // Catch Exception - rethrow as DNS error
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.lookup ENOTFOUND ");
        il.Emit(OpCodes.Ldloc, hostnameLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.EndExceptionBlock();

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsLookupService: resolves address and port to hostname and service.
    /// Signature: object DnsLookupService(object address, object port)
    /// Returns a Dictionary with { hostname: string, service: string }
    /// </summary>
    private void EmitDnsLookupService(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsLookupService",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.DnsLookupService = method;

        var il = method.GetILGenerator();

        // Local variables
        var addressStrLocal = il.DeclareLocal(_types.String);
        var portLocal = il.DeclareLocal(_types.Int32);
        var ipAddressLocal = il.DeclareLocal(typeof(IPAddress));
        var hostEntryLocal = il.DeclareLocal(typeof(IPHostEntry));
        var resultLocal = il.DeclareLocal(_types.Object);

        // Labels
        var parsePortLabel = il.DefineLabel();
        var lookupLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();
        var parseOkLabel = il.DefineLabel();

        // Extract address string from arg0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, addressStrLocal);
        il.Emit(OpCodes.Brtrue, parsePortLabel);

        // Throw if not string
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.lookupService address must be a string");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Extract port from arg1
        il.MarkLabel(parsePortLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, lookupLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, portLocal);

        il.MarkLabel(lookupLabel);

        // Try-catch block
        il.BeginExceptionBlock();

        // Parse IP address
        il.Emit(OpCodes.Ldloc, addressStrLocal);
        il.Emit(OpCodes.Ldloca, ipAddressLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("TryParse", [typeof(string), typeof(IPAddress).MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, parseOkLabel);

        // Throw invalid address
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.lookupService invalid address ");
        il.Emit(OpCodes.Ldloc, addressStrLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(parseOkLabel);

        // Reverse DNS lookup
        il.Emit(OpCodes.Ldloc, ipAddressLocal);
        il.Emit(OpCodes.Call, typeof(Dns).GetMethod("GetHostEntry", [typeof(IPAddress)])!);
        il.Emit(OpCodes.Stloc, hostEntryLocal);

        // Create result object { hostname: string, service: string }
        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var addMethod = _types.GetMethod(dictType, "Add", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        // hostname
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "hostname");
        il.Emit(OpCodes.Ldloc, hostEntryLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPHostEntry).GetProperty("HostName")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, addMethod);

        // service (just port number as string)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "service");
        il.Emit(OpCodes.Ldloca, portLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Call, addMethod);

        // Wrap in TSObject and store in result
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Leave try block
        il.Emit(OpCodes.Leave, returnLabel);

        // Catch Exception
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.lookupService ENOTFOUND ");
        il.Emit(OpCodes.Ldloc, addressStrLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.EndExceptionBlock();

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsGetLookup: returns a TSFunction wrapper for dns.lookup.
    /// Creates both the implementation method and the getter.
    /// </summary>
    private void EmitDnsGetLookup(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First, create the implementation method that takes List<object> args
        var implMethod = typeBuilder.DefineMethod(
            "DnsLookupImpl",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]
        );

        var il = implMethod.GetILGenerator();

        // Extract args[0] (hostname) and args[1] (options) from the list
        var hostnameLocal = il.DeclareLocal(_types.Object);
        var optionsLocal = il.DeclareLocal(_types.Object);

        // hostname = args.Count > 0 ? args[0] : null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Ldc_I4_0);
        var skipHostnameLabel = il.DefineLabel();
        var afterHostnameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipHostnameLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Br, afterHostnameLabel);
        il.MarkLabel(skipHostnameLabel);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(afterHostnameLabel);
        il.Emit(OpCodes.Stloc, hostnameLocal);

        // options = args.Count > 1 ? args[1] : null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Ldc_I4_1);
        var skipOptionsLabel = il.DefineLabel();
        var afterOptionsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipOptionsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Br, afterOptionsLabel);
        il.MarkLabel(skipOptionsLabel);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(afterOptionsLabel);
        il.Emit(OpCodes.Stloc, optionsLocal);

        // Call DnsLookup(hostname, options)
        il.Emit(OpCodes.Ldloc, hostnameLocal);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Call, runtime.DnsLookup);
        il.Emit(OpCodes.Ret);

        // Now create the getter method that returns a TSFunction
        var getterMethod = typeBuilder.DefineMethod(
            "DnsGetLookup",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.DnsGetLookup = getterMethod;

        var getterIl = getterMethod.GetILGenerator();

        // return new TSFunction(null, implMethod)
        getterIl.Emit(OpCodes.Ldnull); // target (static method)
        getterIl.Emit(OpCodes.Ldtoken, implMethod);
        getterIl.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
        getterIl.Emit(OpCodes.Castclass, _types.MethodInfo);
        getterIl.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        getterIl.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsGetLookupService: returns a TSFunction wrapper for dns.lookupService.
    /// Creates both the implementation method and the getter.
    /// </summary>
    private void EmitDnsGetLookupService(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First, create the implementation method that takes List<object> args
        var implMethod = typeBuilder.DefineMethod(
            "DnsLookupServiceImpl",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]
        );

        var il = implMethod.GetILGenerator();

        // Extract args[0] (address) and args[1] (port) from the list
        var addressLocal = il.DeclareLocal(_types.Object);
        var portLocal = il.DeclareLocal(_types.Object);

        // address = args.Count > 0 ? args[0] : null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Ldc_I4_0);
        var skipAddressLabel = il.DefineLabel();
        var afterAddressLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipAddressLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Br, afterAddressLabel);
        il.MarkLabel(skipAddressLabel);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(afterAddressLabel);
        il.Emit(OpCodes.Stloc, addressLocal);

        // port = args.Count > 1 ? args[1] : null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Ldc_I4_1);
        var skipPortLabel = il.DefineLabel();
        var afterPortLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipPortLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Br, afterPortLabel);
        il.MarkLabel(skipPortLabel);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(afterPortLabel);
        il.Emit(OpCodes.Stloc, portLocal);

        // Call DnsLookupService(address, port)
        il.Emit(OpCodes.Ldloc, addressLocal);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Call, runtime.DnsLookupService);
        il.Emit(OpCodes.Ret);

        // Now create the getter method that returns a TSFunction
        var getterMethod = typeBuilder.DefineMethod(
            "DnsGetLookupService",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.DnsGetLookupService = getterMethod;

        var getterIl = getterMethod.GetILGenerator();

        // return new TSFunction(null, implMethod)
        getterIl.Emit(OpCodes.Ldnull); // target (static method)
        getterIl.Emit(OpCodes.Ldtoken, implMethod);
        getterIl.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
        getterIl.Emit(OpCodes.Castclass, _types.MethodInfo);
        getterIl.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        getterIl.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsResolveRecord: resolves DNS records by type (MX, TXT, SRV, etc.).
    /// Now uses emitted wire protocol — no DnsClient dependency.
    /// Signature: object DnsResolveRecord(object hostname, object rrtype)
    /// </summary>
    private void EmitDnsResolveRecord(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsResolveRecord",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.DnsResolveRecord = method;

        var il = method.GetILGenerator();

        var hostnameLocal = il.DeclareLocal(_types.String);   // 0
        var rrtypeLocal = il.DeclareLocal(_types.String);     // 1
        var resultLocal = il.DeclareLocal(_types.Object);     // 2
        var returnLabel = il.DefineLabel();

        // hostname = arg0.ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, hostnameLocal);

        // rrtype = arg1.ToString().ToUpperInvariant()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "ToUpperInvariant"));
        il.Emit(OpCodes.Stloc, rrtypeLocal);

        // Rrtype switch: series of string comparisons
        var mxLabel = il.DefineLabel();
        var txtLabel = il.DefineLabel();
        var srvLabel = il.DefineLabel();
        var cnameLabel = il.DefineLabel();
        var nsLabel = il.DefineLabel();
        var soaLabel = il.DefineLabel();
        var ptrLabel = il.DefineLabel();
        var caaLabel = il.DefineLabel();
        var naptrLabel = il.DefineLabel();
        var aLabel = il.DefineLabel();
        var aaaaLabel = il.DefineLabel();
        var unknownLabel = il.DefineLabel();

        var strEquals = typeof(string).GetMethod("Equals", [typeof(string)])!;

        EmitRrtypeCheck(il, rrtypeLocal, "MX", mxLabel, strEquals);
        EmitRrtypeCheck(il, rrtypeLocal, "TXT", txtLabel, strEquals);
        EmitRrtypeCheck(il, rrtypeLocal, "SRV", srvLabel, strEquals);
        EmitRrtypeCheck(il, rrtypeLocal, "CNAME", cnameLabel, strEquals);
        EmitRrtypeCheck(il, rrtypeLocal, "NS", nsLabel, strEquals);
        EmitRrtypeCheck(il, rrtypeLocal, "SOA", soaLabel, strEquals);
        EmitRrtypeCheck(il, rrtypeLocal, "PTR", ptrLabel, strEquals);
        EmitRrtypeCheck(il, rrtypeLocal, "CAA", caaLabel, strEquals);
        EmitRrtypeCheck(il, rrtypeLocal, "NAPTR", naptrLabel, strEquals);
        EmitRrtypeCheck(il, rrtypeLocal, "A", aLabel, strEquals);
        EmitRrtypeCheck(il, rrtypeLocal, "AAAA", aaaaLabel, strEquals);
        il.Emit(OpCodes.Br, unknownLabel);

        // Wire protocol query types
        const int qtMX = 15, qtTXT = 16, qtSRV = 33, qtCNAME = 5, qtNS = 2;
        const int qtSOA = 6, qtPTR = 12, qtCAA = 257, qtNAPTR = 35;
        const int qtA = 1, qtAAAA = 28;

        // === MX section — DnsDoQuery returns pre-parsed List<object?> of dicts ===
        il.MarkLabel(mxLabel);
        EmitDnsResolveViaWireProtocol(il, runtime, hostnameLocal, resultLocal, qtMX, wrapWithConvertList: true);
        il.Emit(OpCodes.Br, returnLabel);

        // === TXT section ===
        il.MarkLabel(txtLabel);
        EmitDnsResolveViaWireProtocol(il, runtime, hostnameLocal, resultLocal, qtTXT, wrapWithConvertList: true);
        il.Emit(OpCodes.Br, returnLabel);

        // === SRV section ===
        il.MarkLabel(srvLabel);
        EmitDnsResolveViaWireProtocol(il, runtime, hostnameLocal, resultLocal, qtSRV, wrapWithConvertList: true);
        il.Emit(OpCodes.Br, returnLabel);

        // === CNAME section — returns List<object?> of strings ===
        il.MarkLabel(cnameLabel);
        EmitDnsResolveViaWireProtocol(il, runtime, hostnameLocal, resultLocal, qtCNAME, wrapWithConvertList: false);
        il.Emit(OpCodes.Br, returnLabel);

        // === NS section ===
        il.MarkLabel(nsLabel);
        EmitDnsResolveViaWireProtocol(il, runtime, hostnameLocal, resultLocal, qtNS, wrapWithConvertList: false);
        il.Emit(OpCodes.Br, returnLabel);

        // === SOA section — DnsDoQuery returns single Dictionary ===
        il.MarkLabel(soaLabel);
        il.Emit(OpCodes.Ldloc, hostnameLocal);
        il.Emit(OpCodes.Ldc_I4, qtSOA);
        il.Emit(OpCodes.Call, runtime.DnsDoQuery);
        // SOA returns a Dictionary<string, object?> directly, wrap as $Object
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, returnLabel);

        // === PTR section ===
        il.MarkLabel(ptrLabel);
        EmitDnsResolveViaWireProtocol(il, runtime, hostnameLocal, resultLocal, qtPTR, wrapWithConvertList: false);
        il.Emit(OpCodes.Br, returnLabel);

        // === CAA section ===
        il.MarkLabel(caaLabel);
        EmitDnsResolveViaWireProtocol(il, runtime, hostnameLocal, resultLocal, qtCAA, wrapWithConvertList: true);
        il.Emit(OpCodes.Br, returnLabel);

        // === NAPTR section ===
        il.MarkLabel(naptrLabel);
        EmitDnsResolveViaWireProtocol(il, runtime, hostnameLocal, resultLocal, qtNAPTR, wrapWithConvertList: true);
        il.Emit(OpCodes.Br, returnLabel);

        // === A section — wire protocol (List<object?> of IP strings) ===
        il.MarkLabel(aLabel);
        EmitDnsResolveViaWireProtocol(il, runtime, hostnameLocal, resultLocal, qtA, wrapWithConvertList: false);
        il.Emit(OpCodes.Br, returnLabel);

        // === AAAA section — wire protocol (List<object?> of IP strings) ===
        il.MarkLabel(aaaaLabel);
        EmitDnsResolveViaWireProtocol(il, runtime, hostnameLocal, resultLocal, qtAAAA, wrapWithConvertList: false);
        il.Emit(OpCodes.Br, returnLabel);

        // === Unknown rrtype ===
        il.MarkLabel(unknownLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.resolve unknown rrtype: ");
        il.Emit(OpCodes.Ldloc, rrtypeLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // === Return ===
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL to call DnsDoQuery and wrap the result as $Array.
    /// For types with dict results (MX, SRV, CAA, NAPTR, TXT), uses DnsConvertList.
    /// For types with string results (CNAME, NS, PTR), wraps directly as $Array.
    /// </summary>
    private void EmitDnsResolveViaWireProtocol(ILGenerator il, EmittedRuntime runtime,
        LocalBuilder hostnameLocal, LocalBuilder resultLocal, int queryType, bool wrapWithConvertList)
    {
        il.Emit(OpCodes.Ldloc, hostnameLocal);
        il.Emit(OpCodes.Ldc_I4, queryType);
        il.Emit(OpCodes.Call, runtime.DnsDoQuery);
        // DnsDoQuery returns List<object?> for non-SOA types
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        if (wrapWithConvertList)
        {
            il.Emit(OpCodes.Call, runtime.DnsConvertList);
        }
        else
        {
            il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        }
        il.Emit(OpCodes.Stloc, resultLocal);
    }

    /// <summary>
    /// Emits an rrtype string comparison and branch.
    /// </summary>
    private static void EmitRrtypeCheck(ILGenerator il, LocalBuilder rrtypeLocal, string value, Label target, MethodInfo strEquals)
    {
        il.Emit(OpCodes.Ldloc, rrtypeLocal);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Callvirt, strEquals);
        il.Emit(OpCodes.Brtrue, target);
    }

    /// <summary>
    /// Emits DnsDoQuery: orchestrator that builds query, sends, and parses response.
    /// Uses emitted wire protocol helpers — no DnsClient dependency.
    /// Signature: object DnsDoQuery(string hostname, int queryType)
    /// </summary>
    // Emitted mirror of DnsWireProtocol.FindChaseTarget (#1073)
    private MethodBuilder _dnsFindChaseTargetMethod = null!;

    /// <summary>
    /// Emits DnsFindChaseTarget: returns the first CNAME target when the answer
    /// section has no records of the query type but does contain CNAMEs; null
    /// otherwise. Mirrors DnsWireProtocol.FindChaseTarget (#1073).
    /// Signature: string DnsFindChaseTarget(byte[] data, int queryType)
    /// </summary>
    private void EmitDnsFindChaseTarget(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsFindChaseTarget",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [typeof(byte[]), _types.Int32]);
        _dnsFindChaseTargetMethod = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.String);
        var offArrLocal = il.DeclareLocal(typeof(int[]));
        var qdcountLocal = il.DeclareLocal(_types.Int32);
        var ancountLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var typeLocal = il.DeclareLocal(_types.Int32);
        var rdlengthLocal = il.DeclareLocal(_types.Int32);
        var rdataStartLocal = il.DeclareLocal(_types.Int32);
        var offLocal = il.DeclareLocal(_types.Int32);
        var endLabel = il.DefineLabel();
        var retNullFast = il.DefineLabel();

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (data.Length < 12) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, 12);
        il.Emit(OpCodes.Blt, retNullFast);
        // if ((data[3] & 0x0F) != 0) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x0F);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brtrue, retNullFast);
        var scan = il.DefineLabel();
        il.Emit(OpCodes.Br, scan);
        il.MarkLabel(retNullFast);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(scan);
        il.BeginExceptionBlock();

        // qdcount = (data[4] << 8) | data[5]; ancount = (data[6] << 8) | data[7]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, qdcountLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, ancountLocal);

        // offArr = new int[1] { 12 }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Int32);
        il.Emit(OpCodes.Stloc, offArrLocal);
        il.Emit(OpCodes.Ldloc, offArrLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, 12);
        il.Emit(OpCodes.Stelem_I4);

        // skip questions: for q in 0..qdcount { SkipName; offArr[0] += 4 }
        {
            var qLoopTop = il.DefineLabel();
            var qLoopCond = il.DefineLabel();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal);
            il.Emit(OpCodes.Br, qLoopCond);
            il.MarkLabel(qLoopTop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, offArrLocal);
            il.Emit(OpCodes.Call, runtime.DnsSkipName);
            il.Emit(OpCodes.Ldloc, offArrLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, offArrLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stelem_I4);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal);
            il.MarkLabel(qLoopCond);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldloc, qdcountLocal);
            il.Emit(OpCodes.Blt, qLoopTop);
        }

        // answer scan
        {
            var aLoopTop = il.DefineLabel();
            var aLoopCond = il.DefineLabel();
            var nextIter = il.DefineLabel();
            var scanDone = il.DefineLabel();

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal);
            il.Emit(OpCodes.Br, aLoopCond);

            il.MarkLabel(aLoopTop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, offArrLocal);
            il.Emit(OpCodes.Call, runtime.DnsSkipName);

            // off = offArr[0]
            il.Emit(OpCodes.Ldloc, offArrLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Stloc, offLocal);

            // type = (data[off] << 8) | data[off + 1]
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, offLocal);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, offLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Stloc, typeLocal);

            // rdlength = (data[off + 8] << 8) | data[off + 9]
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, offLocal);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, offLocal);
            il.Emit(OpCodes.Ldc_I4, 9);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Stloc, rdlengthLocal);

            // rdataStart = off + 10
            il.Emit(OpCodes.Ldloc, offLocal);
            il.Emit(OpCodes.Ldc_I4, 10);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, rdataStartLocal);

            // if (type == queryType) { result = null; done; } — real answers exist
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Ldarg_1);
            var notMatch = il.DefineLabel();
            il.Emit(OpCodes.Bne_Un, notMatch);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, endLabel);
            il.MarkLabel(notMatch);

            // if (type == 5 (CNAME) && result == null) result = DnsReadName(data, [rdataStart])
            var notCname = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Ldc_I4_5);
            il.Emit(OpCodes.Bne_Un, notCname);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Brtrue, notCname);
            {
                var nameOffLocal = il.DeclareLocal(typeof(int[]));
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Newarr, _types.Int32);
                il.Emit(OpCodes.Stloc, nameOffLocal);
                il.Emit(OpCodes.Ldloc, nameOffLocal);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldloc, rdataStartLocal);
                il.Emit(OpCodes.Stelem_I4);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, nameOffLocal);
                il.Emit(OpCodes.Call, runtime.DnsReadName);
                il.Emit(OpCodes.Stloc, resultLocal);
            }
            il.MarkLabel(notCname);

            // offArr[0] = rdataStart + rdlength
            il.Emit(OpCodes.Ldloc, offArrLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, rdataStartLocal);
            il.Emit(OpCodes.Ldloc, rdlengthLocal);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stelem_I4);

            il.MarkLabel(nextIter);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal);
            il.MarkLabel(aLoopCond);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldloc, ancountLocal);
            il.Emit(OpCodes.Blt, aLoopTop);
            il.MarkLabel(scanDone);
        }

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDnsDoQuery(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsDoQuery",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Int32]);
        runtime.DnsDoQuery = method;

        var il = method.GetILGenerator();
        var nameLocal = il.DeclareLocal(_types.String);
        var depthLocal = il.DeclareLocal(_types.Int32);
        var queryLocal = il.DeclareLocal(typeof(byte[]));
        var responseLocal = il.DeclareLocal(typeof(byte[]));
        var targetLocal = il.DeclareLocal(_types.String);
        var loopTop = il.DefineLabel();
        var parseLabel = il.DefineLabel();

        // name = hostname; depth = 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, nameLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, depthLocal);

        il.MarkLabel(loopTop);

        // byte[] query = DnsBuildQuery(name, queryType)
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.DnsBuildQuery);
        il.Emit(OpCodes.Stloc, queryLocal);

        // byte[] response = DnsSendReceive(query)
        il.Emit(OpCodes.Ldloc, queryLocal);
        il.Emit(OpCodes.Call, runtime.DnsSendReceive);
        il.Emit(OpCodes.Stloc, responseLocal);

        // CNAME chase (#1073): A(1)/AAAA(28) only, bounded depth 8
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        var maybeAaaa = il.DefineLabel();
        var chase = il.DefineLabel();
        il.Emit(OpCodes.Beq, chase);
        il.MarkLabel(maybeAaaa);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, 28);
        il.Emit(OpCodes.Bne_Un, parseLabel);
        il.MarkLabel(chase);
        il.Emit(OpCodes.Ldloc, depthLocal);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Bge, parseLabel);

        // target = DnsFindChaseTarget(response, queryType); if (target == null) parse
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _dnsFindChaseTargetMethod);
        il.Emit(OpCodes.Stloc, targetLocal);
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Brfalse, parseLabel);

        // name = target; depth++; loop
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Stloc, nameLocal);
        il.Emit(OpCodes.Ldloc, depthLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, depthLocal);
        il.Emit(OpCodes.Br, loopTop);

        // return DnsParseResponse(response, queryType, hostname) — original hostname
        il.MarkLabel(parseLabel);
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.DnsParseResponse);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsReadUInt16: reads 2 bytes big-endian from data at offset[0], increments offset[0] by 2.
    /// Signature: int DnsReadUInt16(byte[] data, int[] offset)
    /// </summary>
    private void EmitDnsReadUInt16(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsReadUInt16",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [typeof(byte[]), typeof(int[])]);
        runtime.DnsReadUInt16 = method;

        var il = method.GetILGenerator();

        // int off = offset[0]
        var offLocal = il.DeclareLocal(_types.Int32); // 0
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Stloc, offLocal);

        // int val = (data[off] << 8) | data[off + 1]
        var valLocal = il.DeclareLocal(_types.Int32); // 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, valLocal);

        // offset[0] = off + 2
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);

        // return val
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsReadUInt32: reads 4 bytes big-endian from data at offset[0], increments offset[0] by 4.
    /// Signature: int DnsReadUInt32(byte[] data, int[] offset)
    /// </summary>
    private void EmitDnsReadUInt32(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsReadUInt32",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [typeof(byte[]), typeof(int[])]);
        runtime.DnsReadUInt32 = method;

        var il = method.GetILGenerator();

        // int off = offset[0]
        var offLocal = il.DeclareLocal(_types.Int32); // 0
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Stloc, offLocal);

        // int val = (data[off] << 24) | (data[off+1] << 16) | (data[off+2] << 8) | data[off+3]
        var valLocal = il.DeclareLocal(_types.Int32); // 1

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 24);
        il.Emit(OpCodes.Shl);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);

        il.Emit(OpCodes.Stloc, valLocal);

        // offset[0] = off + 4
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);

        // return val
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsReadCharString: reads a length-prefixed string from data at offset[0].
    /// Signature: string DnsReadCharString(byte[] data, int[] offset)
    /// </summary>
    private void EmitDnsReadCharString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsReadCharString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [typeof(byte[]), typeof(int[])]);
        runtime.DnsReadCharString = method;

        var il = method.GetILGenerator();

        // int off = offset[0]
        var offLocal = il.DeclareLocal(_types.Int32); // 0
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Stloc, offLocal);

        // int len = data[off]
        var lenLocal = il.DeclareLocal(_types.Int32); // 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, lenLocal);

        // off++
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offLocal);

        // string result = Encoding.UTF8.GetString(data, off, len)
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetString", [typeof(byte[]), _types.Int32, _types.Int32])!);
        var resultLocal = il.DeclareLocal(_types.String); // 2
        il.Emit(OpCodes.Stloc, resultLocal);

        // offset[0] = off + len
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsReadExact: reads exact number of bytes from a NetworkStream using async I/O with cancellation.
    /// Signature: void DnsReadExact(NetworkStream stream, byte[] buf, int offset, int count, CancellationToken ct)
    /// </summary>
    private void EmitDnsReadExact(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature: void DnsReadExact(NetworkStream stream, byte[] buf, int offset, int count)
        // Uses Task.Wait(DnsGetTimeoutMs()) for reliable cross-platform timeout.
        var method = typeBuilder.DefineMethod(
            "DnsReadExact",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(System.Net.Sockets.NetworkStream), typeof(byte[]), _types.Int32, _types.Int32]);
        runtime.DnsReadExact = method;

        var il = method.GetILGenerator();

        var taskInt = _types.MakeGenericType(typeof(Task<>), _types.Int32);

        var readLocal = il.DeclareLocal(_types.Int32);       // 0: read count
        var readTaskLocal = il.DeclareLocal(taskInt);         // 1: Task<int>

        // while (count > 0)
        var loopStart = il.DefineLabel();
        var loopCond = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopStart);

        // Task<int> readTask = stream.ReadAsync(buf, offset, count, CancellationToken.None)
        il.Emit(OpCodes.Ldarg_0); // stream
        il.Emit(OpCodes.Ldarg_1); // buf
        il.Emit(OpCodes.Ldarg_2); // offset
        il.Emit(OpCodes.Ldarg_3); // count
        var ctNoneLocal = il.DeclareLocal(typeof(CancellationToken));
        il.Emit(OpCodes.Ldloca, ctNoneLocal);
        il.Emit(OpCodes.Initobj, typeof(CancellationToken));
        il.Emit(OpCodes.Ldloc, ctNoneLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("ReadAsync",
            [typeof(byte[]), _types.Int32, _types.Int32, typeof(CancellationToken)])!);
        il.Emit(OpCodes.Stloc, readTaskLocal);

        // if (!readTask.Wait(DnsGetTimeoutMs())) throw SocketException(TimedOut)
        il.Emit(OpCodes.Ldloc, readTaskLocal);
        il.Emit(OpCodes.Call, runtime.DnsGetTimeoutMs);
        il.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("Wait", [_types.Int32])!);
        var waitOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, waitOkLabel);
        il.Emit(OpCodes.Ldc_I4, (int)SocketError.TimedOut);
        il.Emit(OpCodes.Newobj, typeof(SocketException).GetConstructor([_types.Int32])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(waitOkLabel);

        // int read = readTask.Result
        il.Emit(OpCodes.Ldloc, readTaskLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(taskInt, "Result")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, readLocal);

        // if (read == 0) throw
        il.Emit(OpCodes.Ldloc, readLocal);
        var notZeroLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notZeroLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.resolve connection closed unexpectedly");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notZeroLabel);

        // offset += read
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, readLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Starg, 2);

        // count -= read
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, readLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Starg, 3);

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldarg_3); // count
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, loopStart);

        var doneLabel = il.DefineLabel();

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsEncodeName: encodes a domain name as DNS labels into a List&lt;byte&gt;.
    /// Signature: void DnsEncodeName(List&lt;byte&gt; packet, string name)
    /// </summary>
    private void EmitDnsEncodeName(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsEncodeName",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(List<byte>), _types.String]);
        runtime.DnsEncodeName = method;

        var il = method.GetILGenerator();

        // string trimmed = name.TrimEnd('.')
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Char);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, (int)'.');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "TrimEnd", [typeof(char[])])!);

        // string[] labels = trimmed.Split('.', StringSplitOptions.None)
        il.Emit(OpCodes.Ldc_I4, (int)'.');
        il.Emit(OpCodes.Ldc_I4_0); // StringSplitOptions.None
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Split", [_types.Char, typeof(StringSplitOptions)])!);
        var labelsLocal = il.DeclareLocal(typeof(string[])); // 0
        il.Emit(OpCodes.Stloc, labelsLocal);

        // for (int i = 0; i < labels.Length; i++)
        var indexLocal = il.DeclareLocal(_types.Int32); // 1
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        var loopStart = il.DefineLabel();
        var loopCond = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopStart);

        // byte[] bytes = Encoding.ASCII.GetBytes(labels[i])
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("ASCII")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, labelsLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetBytes", [_types.String])!);
        var bytesLocal = il.DeclareLocal(typeof(byte[])); // 2
        il.Emit(OpCodes.Stloc, bytesLocal);

        // packet.Add((byte)bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("Add")!);

        // packet.AddRange(bytes)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("AddRange", [typeof(IEnumerable<byte>)])!);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, labelsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, loopStart);

        // packet.Add(0x00) — root label
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("Add")!);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsReadName: reads a DNS name with pointer decompression (RFC 1035 §4.1.4).
    /// Signature: string DnsReadName(byte[] data, int[] offset)
    /// </summary>
    private void EmitDnsReadNameMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsReadName",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [typeof(byte[]), typeof(int[])]);
        runtime.DnsReadName = method;

        var il = method.GetILGenerator();

        // var sb = new StringBuilder()
        var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder)); // 0
        il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // bool jumped = false
        var jumpedLocal = il.DeclareLocal(_types.Int32); // 1 (use int as bool)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jumpedLocal);

        // int savedOffset = 0
        var savedOffsetLocal = il.DeclareLocal(_types.Int32); // 2
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, savedOffsetLocal);

        // int maxJumps = 128
        var maxJumpsLocal = il.DeclareLocal(_types.Int32); // 3
        il.Emit(OpCodes.Ldc_I4, 128);
        il.Emit(OpCodes.Stloc, maxJumpsLocal);

        // int off = offset[0]
        var offLocal = il.DeclareLocal(_types.Int32); // 4
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Stloc, offLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);

        // if (maxJumps-- <= 0) break
        il.Emit(OpCodes.Ldloc, maxJumpsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, maxJumpsLocal);
        il.Emit(OpCodes.Ldloc, maxJumpsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, loopEnd);

        // if (off >= data.Length) break
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // int len = data[off]
        var lenLocal = il.DeclareLocal(_types.Int32); // 5
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, lenLocal);

        // if (len == 0) { off++; break; }
        il.Emit(OpCodes.Ldloc, lenLocal);
        var notZeroLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notZeroLabel);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offLocal);
        il.Emit(OpCodes.Br, loopEnd);

        il.MarkLabel(notZeroLabel);

        // if ((len & 0xC0) == 0xC0) -> pointer
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4, 0xC0);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I4, 0xC0);
        var notPointerLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notPointerLabel);

        // Pointer case
        // if (!jumped) savedOffset = off + 2
        il.Emit(OpCodes.Ldloc, jumpedLocal);
        var skipSaveLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipSaveLabel);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, savedOffsetLocal);
        il.MarkLabel(skipSaveLabel);

        // int pointer = ((len & 0x3F) << 8) | data[off + 1]
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4, 0x3F);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, offLocal);

        // jumped = true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, jumpedLocal);

        il.Emit(OpCodes.Br, loopStart); // continue

        // Regular label
        il.MarkLabel(notPointerLabel);

        // off++
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offLocal);

        // if (sb.Length > 0) sb.Append('.')
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetProperty("Length")!.GetGetMethod()!);
        var skipDotLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipDotLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'.');
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [_types.Char])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipDotLabel);

        // sb.Append(Encoding.ASCII.GetString(data, off, len))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("ASCII")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetString", [typeof(byte[]), _types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);

        // off += len
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offLocal);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // if (jumped) offset[0] = savedOffset; else offset[0] = off
        il.Emit(OpCodes.Ldloc, jumpedLocal);
        var notJumpedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notJumpedLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, savedOffsetLocal);
        il.Emit(OpCodes.Stelem_I4);
        var returnLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, returnLabel);

        il.MarkLabel(notJumpedLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Stelem_I4);

        il.MarkLabel(returnLabel);

        // return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(System.Text.StringBuilder), "ToString"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsSkipName: skips a DNS name in the packet (for traversal without reading).
    /// Signature: void DnsSkipName(byte[] data, int[] offset)
    /// </summary>
    private void EmitDnsSkipNameMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsSkipName",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(byte[]), typeof(int[])]);
        runtime.DnsSkipName = method;

        var il = method.GetILGenerator();

        // int off = offset[0]
        var offLocal = il.DeclareLocal(_types.Int32); // 0
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Stloc, offLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);

        // if (off >= data.Length) break
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // int len = data[off]
        var lenLocal = il.DeclareLocal(_types.Int32); // 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, lenLocal);

        // if (len == 0) { off++; break; }
        il.Emit(OpCodes.Ldloc, lenLocal);
        var notZeroLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notZeroLabel);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offLocal);
        il.Emit(OpCodes.Br, loopEnd);

        il.MarkLabel(notZeroLabel);

        // if ((len & 0xC0) == 0xC0) { off += 2; break; }
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4, 0xC0);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I4, 0xC0);
        var notPointerLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notPointerLabel);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offLocal);
        il.Emit(OpCodes.Br, loopEnd);

        il.MarkLabel(notPointerLabel);

        // off += 1 + len
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offLocal);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // offset[0] = off
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offLocal);
        il.Emit(OpCodes.Stelem_I4);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsGetTimeoutMs: per-attempt DNS timeout in milliseconds.
    /// SHARPTS_DNS_TIMEOUT_MS overrides the 5s default — mirrors
    /// DnsWireProtocol.TimeoutMs so fake-server tests can exercise the timeout
    /// path quickly in compiled mode too.
    /// Signature: int DnsGetTimeoutMs()
    /// </summary>
    private void EmitDnsGetTimeoutMsMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsGetTimeoutMs",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            Type.EmptyTypes);
        runtime.DnsGetTimeoutMs = method;

        var il = method.GetILGenerator();

        var envLocal = il.DeclareLocal(_types.String);
        var valueLocal = il.DeclareLocal(_types.Int32);
        var defaultLabel = il.DefineLabel();

        // string env = Environment.GetEnvironmentVariable("SHARPTS_DNS_TIMEOUT_MS")
        il.Emit(OpCodes.Ldstr, "SHARPTS_DNS_TIMEOUT_MS");
        il.Emit(OpCodes.Call, typeof(Environment).GetMethod("GetEnvironmentVariable", [_types.String])!);
        il.Emit(OpCodes.Stloc, envLocal);

        // if (env == null || !int.TryParse(env, out value) || value <= 0) goto default
        il.Emit(OpCodes.Ldloc, envLocal);
        il.Emit(OpCodes.Brfalse, defaultLabel);

        il.Emit(OpCodes.Ldloc, envLocal);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", [_types.String, _types.Int32.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, defaultLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, defaultLabel);

        // return value
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        // return 5000
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldc_I4, 5000);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsParseServerEndpoint: parses a DNS server address that may carry a
    /// port ("8.8.8.8", "127.0.0.1:5353", "[::1]:5353"); a missing port defaults
    /// to 53. Mirrors the port handling in DnsWireProtocol.SendReceive.
    /// Signature: IPEndPoint DnsParseServerEndpoint(string server)
    /// </summary>
    private void EmitDnsParseServerEndpointMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsParseServerEndpoint",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(IPEndPoint),
            [_types.String]);
        runtime.DnsParseServerEndpoint = method;

        var il = method.GetILGenerator();

        var endpointLocal = il.DeclareLocal(typeof(IPEndPoint));
        var doneLabel = il.DefineLabel();

        // var endpoint = IPEndPoint.Parse(server)  — a bare address parses with Port == 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(IPEndPoint).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Stloc, endpointLocal);

        // if (endpoint.Port == 0) endpoint.Port = 53
        il.Emit(OpCodes.Ldloc, endpointLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, doneLabel);

        il.Emit(OpCodes.Ldloc, endpointLocal);
        il.Emit(OpCodes.Ldc_I4, 53);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Port")!.GetSetMethod()!);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, endpointLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsGetSystemDns: returns the system's primary DNS server address as string.
    /// SHARPTS_DNS_SERVER overrides discovery (mirrors DnsWireProtocol.GetSystemDnsServer).
    /// Signature: string DnsGetSystemDns()
    /// </summary>
    private void EmitDnsGetSystemDnsMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsGetSystemDns",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            Type.EmptyTypes);
        runtime.DnsGetSystemDns = method;

        var il = method.GetILGenerator();

        var resultLocal = il.DeclareLocal(_types.String); // 0
        var returnLabel = il.DefineLabel();

        // string env = Environment.GetEnvironmentVariable("SHARPTS_DNS_SERVER");
        // if (!string.IsNullOrEmpty(env)) return env;
        var envLocal = il.DeclareLocal(_types.String);
        var noOverrideLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldstr, "SHARPTS_DNS_SERVER");
        il.Emit(OpCodes.Call, typeof(Environment).GetMethod("GetEnvironmentVariable", [_types.String])!);
        il.Emit(OpCodes.Stloc, envLocal);
        il.Emit(OpCodes.Ldloc, envLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "IsNullOrEmpty", [_types.String])!);
        il.Emit(OpCodes.Brtrue, noOverrideLabel);
        il.Emit(OpCodes.Ldloc, envLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noOverrideLabel);

        il.BeginExceptionBlock();

        // First pass: prefer IPv4
        // var interfaces = NetworkInterface.GetAllNetworkInterfaces()
        var niArrayLocal = il.DeclareLocal(typeof(System.Net.NetworkInformation.NetworkInterface[])); // 1
        il.Emit(OpCodes.Call, typeof(System.Net.NetworkInformation.NetworkInterface).GetMethod("GetAllNetworkInterfaces")!);
        il.Emit(OpCodes.Stloc, niArrayLocal);

        // for (int i = 0; i < interfaces.Length; i++)
        var iLocal = il.DeclareLocal(_types.Int32); // 2
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var pass1LoopStart = il.DefineLabel();
        var pass1LoopCond = il.DefineLabel();
        il.Emit(OpCodes.Br, pass1LoopCond);

        il.MarkLabel(pass1LoopStart);

        // if (ni.OperationalStatus != Up) continue
        il.Emit(OpCodes.Ldloc, niArrayLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.NetworkInterface).GetProperty("OperationalStatus")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)System.Net.NetworkInformation.OperationalStatus.Up);
        var pass1ContinueLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, pass1ContinueLabel);

        // var props = ni.GetIPProperties()
        il.Emit(OpCodes.Ldloc, niArrayLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.NetworkInterface).GetMethod("GetIPProperties")!);
        var propsLocal = il.DeclareLocal(typeof(System.Net.NetworkInformation.IPInterfaceProperties)); // 3
        il.Emit(OpCodes.Stloc, propsLocal);

        // var dnsAddrs = props.DnsAddresses
        il.Emit(OpCodes.Ldloc, propsLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.IPInterfaceProperties).GetProperty("DnsAddresses")!.GetGetMethod()!);
        var dnsAddrsLocal = il.DeclareLocal(typeof(System.Net.NetworkInformation.IPAddressCollection)); // 4
        il.Emit(OpCodes.Stloc, dnsAddrsLocal);

        // for (int j = 0; j < dnsAddrs.Count; j++)
        var jLocal = il.DeclareLocal(_types.Int32); // 5
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);
        var innerLoopStart = il.DefineLabel();
        var innerLoopCond = il.DefineLabel();
        il.Emit(OpCodes.Br, innerLoopCond);

        il.MarkLabel(innerLoopStart);

        // if (dnsAddrs[j].AddressFamily == InterNetwork)
        il.Emit(OpCodes.Ldloc, dnsAddrsLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.IPAddressCollection).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetwork);
        var innerContinueLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, innerContinueLabel);

        // Found IPv4 DNS
        il.Emit(OpCodes.Ldloc, dnsAddrsLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.IPAddressCollection).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IPAddress), "ToString"));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, returnLabel);

        il.MarkLabel(innerContinueLabel);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);

        il.MarkLabel(innerLoopCond);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, dnsAddrsLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.IPAddressCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, innerLoopStart);

        il.MarkLabel(pass1ContinueLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(pass1LoopCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, niArrayLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, pass1LoopStart);

        // Second pass: accept any address family
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var pass2LoopStart = il.DefineLabel();
        var pass2LoopCond = il.DefineLabel();
        il.Emit(OpCodes.Br, pass2LoopCond);

        il.MarkLabel(pass2LoopStart);

        // if (ni.OperationalStatus != Up) continue
        il.Emit(OpCodes.Ldloc, niArrayLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.NetworkInterface).GetProperty("OperationalStatus")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)System.Net.NetworkInformation.OperationalStatus.Up);
        var pass2ContinueLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, pass2ContinueLabel);

        // var props2 = ni.GetIPProperties()
        il.Emit(OpCodes.Ldloc, niArrayLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.NetworkInterface).GetMethod("GetIPProperties")!);
        il.Emit(OpCodes.Stloc, propsLocal);

        // var dnsAddrs2 = props2.DnsAddresses
        il.Emit(OpCodes.Ldloc, propsLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.IPInterfaceProperties).GetProperty("DnsAddresses")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, dnsAddrsLocal);

        // if (dnsAddrs2.Count > 0) return dnsAddrs2[0].ToString()
        il.Emit(OpCodes.Ldloc, dnsAddrsLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.IPAddressCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, pass2ContinueLabel);

        il.Emit(OpCodes.Ldloc, dnsAddrsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.NetworkInformation.IPAddressCollection).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IPAddress), "ToString"));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, returnLabel);

        il.MarkLabel(pass2ContinueLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(pass2LoopCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, niArrayLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, pass2LoopStart);

        // Fallback: "8.8.8.8"
        il.Emit(OpCodes.Ldstr, "8.8.8.8");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, returnLabel);

        // catch — fallback to "8.8.8.8"
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "8.8.8.8");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, returnLabel);

        il.EndExceptionBlock();

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsBuildQuery: builds a DNS query packet per RFC 1035.
    /// Signature: byte[] DnsBuildQuery(string hostname, int queryType)
    /// </summary>
    private void EmitDnsBuildQueryMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsBuildQuery",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(byte[]),
            [_types.String, _types.Int32]);
        runtime.DnsBuildQuery = method;

        var il = method.GetILGenerator();

        // var packet = new List<byte>(64)
        var packetLocal = il.DeclareLocal(typeof(List<byte>)); // 0
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Newobj, typeof(List<byte>).GetConstructor([_types.Int32])!);
        il.Emit(OpCodes.Stloc, packetLocal);

        // Transaction ID (random)
        // var rng = new Random()
        var rngLocal = il.DeclareLocal(typeof(Random)); // 1
        il.Emit(OpCodes.Newobj, typeof(Random).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, rngLocal);

        // int id = rng.Next(0, 65536)
        var idLocal = il.DeclareLocal(_types.Int32); // 2
        il.Emit(OpCodes.Ldloc, rngLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, 65536);
        il.Emit(OpCodes.Callvirt, typeof(Random).GetMethod("Next", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, idLocal);

        // packet.Add((byte)(id >> 8))
        il.Emit(OpCodes.Ldloc, packetLocal);
        il.Emit(OpCodes.Ldloc, idLocal);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shr);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("Add")!);

        // packet.Add((byte)(id & 0xFF))
        il.Emit(OpCodes.Ldloc, packetLocal);
        il.Emit(OpCodes.Ldloc, idLocal);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("Add")!);

        // Flags: 0x01 0x00 (standard query, recursion desired)
        EmitListByteAdd(il, packetLocal, 0x01);
        EmitListByteAdd(il, packetLocal, 0x00);

        // QDCOUNT = 1
        EmitListByteAdd(il, packetLocal, 0x00);
        EmitListByteAdd(il, packetLocal, 0x01);

        // ANCOUNT = 0
        EmitListByteAdd(il, packetLocal, 0x00);
        EmitListByteAdd(il, packetLocal, 0x00);

        // NSCOUNT = 0
        EmitListByteAdd(il, packetLocal, 0x00);
        EmitListByteAdd(il, packetLocal, 0x00);

        // ARCOUNT = 0
        EmitListByteAdd(il, packetLocal, 0x00);
        EmitListByteAdd(il, packetLocal, 0x00);

        // EncodeName(packet, hostname)
        il.Emit(OpCodes.Ldloc, packetLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.DnsEncodeName);

        // QTYPE (2 bytes, big-endian)
        il.Emit(OpCodes.Ldloc, packetLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shr);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("Add")!);

        il.Emit(OpCodes.Ldloc, packetLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("Add")!);

        // QCLASS = IN (1)
        EmitListByteAdd(il, packetLocal, 0x00);
        EmitListByteAdd(il, packetLocal, 0x01);

        // return packet.ToArray()
        il.Emit(OpCodes.Ldloc, packetLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("ToArray")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: emits packet.Add((byte)value) for a constant byte.
    /// </summary>
    private static void EmitListByteAdd(ILGenerator il, LocalBuilder packetLocal, byte value)
    {
        il.Emit(OpCodes.Ldloc, packetLocal);
        il.Emit(OpCodes.Ldc_I4, (int)value);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("Add")!);
    }

    /// <summary>
    /// Emits DnsSendViaTcp: sends a DNS query via TCP with 2-byte length prefix.
    /// Signature: byte[] DnsSendViaTcp(byte[] query, string server)
    /// </summary>
    private void EmitDnsSendViaTcpMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsSendViaTcp",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(byte[]),
            [typeof(byte[]), _types.String]);
        runtime.DnsSendViaTcp = method;

        var il = method.GetILGenerator();

        var resultLocal = il.DeclareLocal(typeof(byte[])); // 0
        var returnLabel = il.DefineLabel();

        // var endpoint = DnsParseServerEndpoint(server) — handles "host" and "host:port"
        var endpointLocal = il.DeclareLocal(typeof(IPEndPoint));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.DnsParseServerEndpoint);
        il.Emit(OpCodes.Stloc, endpointLocal);

        // using var tcp = new TcpClient()
        var tcpLocal = il.DeclareLocal(typeof(TcpClient)); // 1
        il.Emit(OpCodes.Newobj, typeof(TcpClient).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tcpLocal);

        il.BeginExceptionBlock();

        // tcp.SendTimeout = DnsGetTimeoutMs()
        il.Emit(OpCodes.Ldloc, tcpLocal);
        il.Emit(OpCodes.Call, runtime.DnsGetTimeoutMs);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetProperty("SendTimeout")!.GetSetMethod()!);

        // tcp.ConnectAsync(endpoint.Address, endpoint.Port).WaitAsync(TimeSpan.FromMilliseconds(DnsGetTimeoutMs())).GetAwaiter().GetResult()
        il.Emit(OpCodes.Ldloc, tcpLocal);
        il.Emit(OpCodes.Ldloc, endpointLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Address")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, endpointLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("ConnectAsync", [typeof(IPAddress), _types.Int32])!);
        // Task.WaitAsync(TimeSpan) for reliable timeout
        il.Emit(OpCodes.Call, runtime.DnsGetTimeoutMs);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, typeof(TimeSpan).GetMethod("FromMilliseconds", [typeof(double)])!);
        il.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("WaitAsync", [typeof(TimeSpan)])!);
        var connectAwaiterLocal = il.DeclareLocal(typeof(TaskAwaiter));
        il.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("GetAwaiter")!);
        il.Emit(OpCodes.Stloc, connectAwaiterLocal);
        il.Emit(OpCodes.Ldloca, connectAwaiterLocal);
        il.Emit(OpCodes.Call, typeof(TaskAwaiter).GetMethod("GetResult")!);

        // var stream = tcp.GetStream()
        var streamLocal = il.DeclareLocal(typeof(System.Net.Sockets.NetworkStream)); // 2
        il.Emit(OpCodes.Ldloc, tcpLocal);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("GetStream")!);
        il.Emit(OpCodes.Stloc, streamLocal);

        // Write 2-byte length prefix
        // var lengthPrefix = new byte[2]
        var prefixLocal = il.DeclareLocal(typeof(byte[])); // 3
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, prefixLocal);

        // lengthPrefix[0] = (byte)(query.Length >> 8)
        il.Emit(OpCodes.Ldloc, prefixLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shr);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // lengthPrefix[1] = (byte)(query.Length & 0xFF)
        il.Emit(OpCodes.Ldloc, prefixLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // stream.Write(lengthPrefix, 0, 2)
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldloc, prefixLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Write", [typeof(byte[]), _types.Int32, _types.Int32])!);

        // stream.Write(query, 0, query.Length)
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Write", [typeof(byte[]), _types.Int32, _types.Int32])!);

        // stream.Flush()
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Flush", Type.EmptyTypes)!);

        // Read 2-byte response length
        // var respLenBuf = new byte[2]
        var respLenBufLocal = il.DeclareLocal(typeof(byte[])); // 4
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, respLenBufLocal);

        // DnsReadExact(stream, respLenBuf, 0, 2)
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldloc, respLenBufLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, runtime.DnsReadExact);

        // int respLen = (respLenBuf[0] << 8) | respLenBuf[1]
        var respLenLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, respLenBufLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldloc, respLenBufLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, respLenLocal);

        // var response = new byte[respLen]
        il.Emit(OpCodes.Ldloc, respLenLocal);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, resultLocal);

        // DnsReadExact(stream, response, 0, respLen)
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, respLenLocal);
        il.Emit(OpCodes.Call, runtime.DnsReadExact);

        il.Emit(OpCodes.Leave, returnLabel);

        // catch (TimeoutException) → convert to SocketException for outer retry
        il.BeginCatchBlock(typeof(TimeoutException));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4, (int)SocketError.TimedOut);
        il.Emit(OpCodes.Newobj, typeof(SocketException).GetConstructor([_types.Int32])!);
        il.Emit(OpCodes.Throw);

        // finally: tcp.Dispose()
        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloc, tcpLocal);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("Dispose", Type.EmptyTypes)!);
        il.EndExceptionBlock();

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsSendReceive: sends DNS query via UDP, falls back to TCP on truncation, with retry loop.
    /// Signature: byte[] DnsSendReceive(byte[] query)
    /// </summary>
    private void EmitDnsSendReceiveMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsSendReceive",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(byte[]),
            [typeof(byte[])]);
        runtime.DnsSendReceive = method;

        var il = method.GetILGenerator();

        // string dnsServer = DnsGetSystemDns()
        var serverLocal = il.DeclareLocal(_types.String); // 0
        il.Emit(OpCodes.Call, runtime.DnsGetSystemDns);
        il.Emit(OpCodes.Stloc, serverLocal);

        // var endpoint = DnsParseServerEndpoint(server) — handles "host" and "host:port"
        var endpointLocal = il.DeclareLocal(typeof(IPEndPoint)); // 1
        il.Emit(OpCodes.Ldloc, serverLocal);
        il.Emit(OpCodes.Call, runtime.DnsParseServerEndpoint);
        il.Emit(OpCodes.Stloc, endpointLocal);

        // byte[] result = null
        var resultLocal = il.DeclareLocal(typeof(byte[])); // 2
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (int attempt = 0; attempt <= 2; attempt++)
        var attemptLocal = il.DeclareLocal(_types.Int32); // 3
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, attemptLocal);

        var retryLoopStart = il.DefineLabel();
        var retryLoopCond = il.DefineLabel();
        var retryIncrement = il.DefineLabel();
        var returnLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, retryLoopCond);

        il.MarkLabel(retryLoopStart);

        // try
        il.BeginExceptionBlock();

        // using var udp = new UdpClient()
        var udpLocal = il.DeclareLocal(typeof(UdpClient)); // 4
        il.Emit(OpCodes.Newobj, typeof(UdpClient).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, udpLocal);

        // udp.Client.SendTimeout = DnsGetTimeoutMs()
        il.Emit(OpCodes.Ldloc, udpLocal);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.DnsGetTimeoutMs);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty("SendTimeout")!.GetSetMethod()!);

        // udp.Send(query, query.Length, endpoint)
        il.Emit(OpCodes.Ldloc, udpLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, endpointLocal);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("Send", [typeof(byte[]), _types.Int32, typeof(IPEndPoint)])!);
        il.Emit(OpCodes.Pop); // discard send count

        // Use Task.Wait(timeout) for reliable cross-platform timeout.
        // Neither Socket.ReceiveTimeout (SO_RCVTIMEO) nor CancellationToken-based
        // cancellation of ReceiveAsync works reliably on macOS ARM64
        // (dotnet/runtime#81378, #64551).

        var udpReceiveResultType = typeof(UdpReceiveResult);
        var valueTaskType = _types.MakeGenericType(typeof(ValueTask<>), udpReceiveResultType);
        var taskType = _types.MakeGenericType(typeof(Task<>), udpReceiveResultType);

        // ValueTask<UdpReceiveResult> vt = udp.ReceiveAsync(CancellationToken.None)
        var vtLocal = il.DeclareLocal(valueTaskType); // 5
        var ctNoneLocal = il.DeclareLocal(typeof(CancellationToken)); // 6
        il.Emit(OpCodes.Ldloca, ctNoneLocal);
        il.Emit(OpCodes.Initobj, typeof(CancellationToken));
        il.Emit(OpCodes.Ldloc, udpLocal);
        il.Emit(OpCodes.Ldloc, ctNoneLocal);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("ReceiveAsync", [typeof(CancellationToken)])!);
        il.Emit(OpCodes.Stloc, vtLocal);

        // Task<UdpReceiveResult> task = vt.AsTask()
        var taskLocal = il.DeclareLocal(taskType); // 7
        il.Emit(OpCodes.Ldloca, vtLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(valueTaskType, "AsTask")!);
        il.Emit(OpCodes.Stloc, taskLocal);

        // if (!task.Wait(DnsGetTimeoutMs())) { udp.Close(); throw SocketException(TimedOut); }
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Call, runtime.DnsGetTimeoutMs);
        il.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("Wait", [_types.Int32])!);
        var waitOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, waitOkLabel);

        // Timeout: close socket and throw
        il.Emit(OpCodes.Ldloc, udpLocal);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("Close", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldc_I4, (int)SocketError.TimedOut);
        il.Emit(OpCodes.Newobj, typeof(SocketException).GetConstructor([_types.Int32])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(waitOkLabel);

        // UdpReceiveResult udpResult = task.Result
        var udpResultLocal = il.DeclareLocal(udpReceiveResultType); // 8
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(taskType, "Result")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, udpResultLocal);

        // byte[] response = udpResult.Buffer
        var responseLocal = il.DeclareLocal(typeof(byte[])); // 9
        il.Emit(OpCodes.Ldloca, udpResultLocal);
        il.Emit(OpCodes.Call, udpReceiveResultType.GetProperty("Buffer")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, responseLocal);

        // udp.Dispose()
        il.Emit(OpCodes.Ldloc, udpLocal);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("Dispose", Type.EmptyTypes)!);

        // Discard datagrams whose transaction ID doesn't echo the query's (stale
        // or spoofed response) and retry; if no matching response ever arrives
        // this falls out of the retry loop into the ETIMEOUT throw — mirrors
        // DnsWireProtocol.SendReceive.
        // if (response.Length < 2 || response[0] != query[0] || response[1] != query[1]) continue;
        var idMismatchLabel = il.DefineLabel();
        var idOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, idMismatchLabel);

        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Bne_Un, idMismatchLabel);

        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Beq, idOkLabel);

        il.MarkLabel(idMismatchLabel);
        il.Emit(OpCodes.Leave, retryIncrement);

        il.MarkLabel(idOkLabel);

        // Check TC (truncation) bit — byte[2], bit 1
        // if (response.Length >= 3 && (response[2] & 0x02) != 0) -> TCP fallback
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_3);
        var noTcCheckLabel = il.DefineLabel();
        il.Emit(OpCodes.Blt, noTcCheckLabel);

        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, noTcCheckLabel);

        // TCP fallback: result = DnsSendViaTcp(query, server)
        var rcodeCheckLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, serverLocal);
        il.Emit(OpCodes.Call, runtime.DnsSendViaTcp);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, rcodeCheckLabel);

        il.MarkLabel(noTcCheckLabel);

        // No truncation: result = response
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Stloc, resultLocal);

        // SERVFAIL/REFUSED are usually transient resolver conditions — retry
        // them like socket errors (mirrors DnsWireProtocol.SendReceive). On the
        // last attempt the response is returned so parsing surfaces the error.
        // if (attempt < 2 && result.Length >= 4 && ((result[3] & 0x0F) == 2 || (result[3] & 0x0F) == 5)) continue;
        il.MarkLabel(rcodeCheckLabel);
        var acceptResponseLabel = il.DefineLabel();
        var retryRcodeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, attemptLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bge, acceptResponseLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Blt, acceptResponseLabel);

        var rcodeLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x0F);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Stloc, rcodeLocal);

        il.Emit(OpCodes.Ldloc, rcodeLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, retryRcodeLabel);
        il.Emit(OpCodes.Ldloc, rcodeLocal);
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Bne_Un, acceptResponseLabel);

        il.MarkLabel(retryRcodeLabel);
        il.Emit(OpCodes.Leave, retryIncrement);

        il.MarkLabel(acceptResponseLabel);
        il.Emit(OpCodes.Leave, returnLabel);

        // catch (SocketException) when (attempt < MaxRetries)
        il.BeginCatchBlock(typeof(SocketException));
        il.Emit(OpCodes.Pop);
        // if (attempt >= 2) rethrow by throwing timeout error
        il.Emit(OpCodes.Ldloc, attemptLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        var retryLabel = il.DefineLabel();
        il.Emit(OpCodes.Blt, retryLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.resolve ETIMEOUT DNS query timed out");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(retryLabel);
        il.Emit(OpCodes.Leave, retryIncrement); // continue to next attempt

        il.EndExceptionBlock();

        il.MarkLabel(retryIncrement);
        // attempt++
        il.Emit(OpCodes.Ldloc, attemptLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, attemptLocal);

        il.MarkLabel(retryLoopCond);
        il.Emit(OpCodes.Ldloc, attemptLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ble, retryLoopStart);

        // If we exhausted retries without returning, throw
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.resolve ETIMEOUT DNS query timed out");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsParseRecord: parses a single DNS record RDATA based on type.
    /// Signature: object DnsParseRecord(byte[] data, int[] offset, int type, int rdlength, int rdataStart)
    /// </summary>
    private void EmitDnsParseRecordMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsParseRecord",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [typeof(byte[]), typeof(int[]), _types.Int32, _types.Int32, _types.Int32]);
        runtime.DnsParseRecord = method;

        var il = method.GetILGenerator();

        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var dictAdd = _types.GetMethod(dictType, "Add", _types.String, _types.Object);

        var resultLocal = il.DeclareLocal(_types.Object);   // 0
        var returnLabel = il.DefineLabel();

        // Labels for each record type
        var typeALabel = il.DefineLabel();
        var typeAAAALabel = il.DefineLabel();
        var typeMXLabel = il.DefineLabel();
        var typeTXTLabel = il.DefineLabel();
        var typeSRVLabel = il.DefineLabel();
        var typeCNAMELabel = il.DefineLabel();
        var typeNSLabel = il.DefineLabel();
        var typeSOALabel = il.DefineLabel();
        var typePTRLabel = il.DefineLabel();
        var typeCAALabel = il.DefineLabel();
        var typeNAPTRLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Switch on type (arg2)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1); // TypeA
        il.Emit(OpCodes.Beq, typeALabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, 28); // TypeAAAA
        il.Emit(OpCodes.Beq, typeAAAALabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, 15); // TypeMX
        il.Emit(OpCodes.Beq, typeMXLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, 16); // TypeTXT
        il.Emit(OpCodes.Beq, typeTXTLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, 33); // TypeSRV
        il.Emit(OpCodes.Beq, typeSRVLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_5); // TypeCNAME
        il.Emit(OpCodes.Beq, typeCNAMELabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_2); // TypeNS
        il.Emit(OpCodes.Beq, typeNSLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_6); // TypeSOA
        il.Emit(OpCodes.Beq, typeSOALabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, 12); // TypePTR
        il.Emit(OpCodes.Beq, typePTRLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, 257); // TypeCAA
        il.Emit(OpCodes.Beq, typeCAALabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, 35); // TypeNAPTR
        il.Emit(OpCodes.Beq, typeNAPTRLabel);

        il.Emit(OpCodes.Br, defaultLabel);

        // === TypeA (1): 4 bytes -> IPAddress -> ToString() ===
        il.MarkLabel(typeALabel);
        {
            il.Emit(OpCodes.Ldarg_3); // rdlength
            il.Emit(OpCodes.Ldc_I4_4);
            var aOkLabel = il.DefineLabel();
            il.Emit(OpCodes.Bge, aOkLabel);
            EmitSetOffsetAndReturnNull(il, returnLabel, resultLocal);
            il.MarkLabel(aOkLabel);

            var ipBytesLocal = il.DeclareLocal(typeof(byte[])); // 1
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, ipBytesLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldloc, ipBytesLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);

            il.Emit(OpCodes.Ldloc, ipBytesLocal);
            il.Emit(OpCodes.Newobj, typeof(IPAddress).GetConstructor([typeof(byte[])])!);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IPAddress), "ToString"));
            il.Emit(OpCodes.Stloc, resultLocal);

            EmitSetOffsetToRdataEnd(il);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === TypeAAAA (28): 16 bytes -> IPAddress -> ToString() ===
        il.MarkLabel(typeAAAALabel);
        {
            il.Emit(OpCodes.Ldarg_3); // rdlength
            il.Emit(OpCodes.Ldc_I4, 16);
            var aaaaOkLabel = il.DefineLabel();
            il.Emit(OpCodes.Bge, aaaaOkLabel);
            EmitSetOffsetAndReturnNull(il, returnLabel, resultLocal);
            il.MarkLabel(aaaaOkLabel);

            var ipBytesLocal = il.DeclareLocal(typeof(byte[]));
            il.Emit(OpCodes.Ldc_I4, 16);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, ipBytesLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldloc, ipBytesLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4, 16);
            il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);

            il.Emit(OpCodes.Ldloc, ipBytesLocal);
            il.Emit(OpCodes.Newobj, typeof(IPAddress).GetConstructor([typeof(byte[])])!);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IPAddress), "ToString"));
            il.Emit(OpCodes.Stloc, resultLocal);

            EmitSetOffsetToRdataEnd(il);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === TypeMX (15): preference(2) + ReadName -> Dictionary { exchange, priority } ===
        il.MarkLabel(typeMXLabel);
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt16);
            var prefLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Stloc, prefLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadName);
            var exchangeLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Stloc, exchangeLocal);

            il.Emit(OpCodes.Newobj, dictCtor);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "exchange");
            il.Emit(OpCodes.Ldloc, exchangeLocal);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "priority");
            il.Emit(OpCodes.Ldloc, prefLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Stloc, resultLocal);

            EmitSetOffsetToRdataEnd(il);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === TypeTXT (16): sequence of length-prefixed strings -> List<object?> ===
        il.MarkLabel(typeTXTLabel);
        {
            var chunksLocal = il.DeclareLocal(_types.ListOfObject);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, chunksLocal);

            var endLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg, 4); // rdataStart
            il.Emit(OpCodes.Ldarg_3); // rdlength
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, endLocal);

            var txtLoopStart = il.DefineLabel();
            var txtLoopCond = il.DefineLabel();
            il.Emit(OpCodes.Br, txtLoopCond);

            il.MarkLabel(txtLoopStart);

            // int strLen = data[offset[0]]
            var strLenLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Stloc, strLenLocal);

            // offset[0]++
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stelem_I4);

            // string text = Encoding.UTF8.GetString(data, offset[0], strLen)
            var textLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldloc, strLenLocal);
            il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetString", [typeof(byte[]), _types.Int32, _types.Int32])!);
            il.Emit(OpCodes.Stloc, textLocal);

            // chunks.Add(text)
            il.Emit(OpCodes.Ldloc, chunksLocal);
            il.Emit(OpCodes.Ldloc, textLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

            // offset[0] += strLen
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldloc, strLenLocal);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stelem_I4);

            il.MarkLabel(txtLoopCond);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldloc, endLocal);
            il.Emit(OpCodes.Blt, txtLoopStart);

            // offset[0] = end
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, endLocal);
            il.Emit(OpCodes.Stelem_I4);

            il.Emit(OpCodes.Ldloc, chunksLocal);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === TypeSRV (33): priority(2) + weight(2) + port(2) + ReadName -> Dictionary ===
        il.MarkLabel(typeSRVLabel);
        {
            var priorityLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt16);
            il.Emit(OpCodes.Stloc, priorityLocal);

            var weightLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt16);
            il.Emit(OpCodes.Stloc, weightLocal);

            var portLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt16);
            il.Emit(OpCodes.Stloc, portLocal);

            var targetLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadName);
            il.Emit(OpCodes.Stloc, targetLocal);

            il.Emit(OpCodes.Newobj, dictCtor);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "name");
            il.Emit(OpCodes.Ldloc, targetLocal);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "port");
            il.Emit(OpCodes.Ldloc, portLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "priority");
            il.Emit(OpCodes.Ldloc, priorityLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "weight");
            il.Emit(OpCodes.Ldloc, weightLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Stloc, resultLocal);

            EmitSetOffsetToRdataEnd(il);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === TypeCNAME (5): ReadName -> string ===
        il.MarkLabel(typeCNAMELabel);
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadName);
            il.Emit(OpCodes.Stloc, resultLocal);
            EmitSetOffsetToRdataEnd(il);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === TypeNS (2): ReadName -> string ===
        il.MarkLabel(typeNSLabel);
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadName);
            il.Emit(OpCodes.Stloc, resultLocal);
            EmitSetOffsetToRdataEnd(il);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === TypeSOA (6): mname + rname + 5xUInt32 -> Dictionary ===
        il.MarkLabel(typeSOALabel);
        {
            var mnameLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadName);
            il.Emit(OpCodes.Stloc, mnameLocal);

            var rnameLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadName);
            il.Emit(OpCodes.Stloc, rnameLocal);

            var serialLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt32);
            il.Emit(OpCodes.Stloc, serialLocal);

            var refreshLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt32);
            il.Emit(OpCodes.Stloc, refreshLocal);

            var retryLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt32);
            il.Emit(OpCodes.Stloc, retryLocal);

            var expireLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt32);
            il.Emit(OpCodes.Stloc, expireLocal);

            var minimumLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt32);
            il.Emit(OpCodes.Stloc, minimumLocal);

            il.Emit(OpCodes.Newobj, dictCtor);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "nsname");
            il.Emit(OpCodes.Ldloc, mnameLocal);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "hostmaster");
            il.Emit(OpCodes.Ldloc, rnameLocal);
            il.Emit(OpCodes.Call, dictAdd);
            // The 32-bit SOA fields are unsigned — Conv_R_Un treats the int32
            // bits as unsigned so serials >= 2^31 don't go negative (#1073).
            foreach (var (fieldName, fieldLocal) in new[]
            {
                ("serial", serialLocal), ("refresh", refreshLocal),
                ("retry", retryLocal), ("expire", expireLocal), ("minttl", minimumLocal)
            })
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldstr, fieldName);
                il.Emit(OpCodes.Ldloc, fieldLocal);
                il.Emit(OpCodes.Conv_R_Un);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Box, _types.Double);
                il.Emit(OpCodes.Call, dictAdd);
            }
            il.Emit(OpCodes.Stloc, resultLocal);

            EmitSetOffsetToRdataEnd(il);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === TypePTR (12): ReadName -> string ===
        il.MarkLabel(typePTRLabel);
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadName);
            il.Emit(OpCodes.Stloc, resultLocal);
            EmitSetOffsetToRdataEnd(il);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === TypeCAA (257): flags(1) + tagLen(1) + tag + value -> Dictionary ===
        il.MarkLabel(typeCAALabel);
        {
            // int flags = data[offset[0]]
            var flagsLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Stloc, flagsLocal);

            // offset[0]++
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stelem_I4);

            // int tagLen = data[offset[0]]
            var tagLenLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Stloc, tagLenLocal);

            // offset[0]++
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stelem_I4);

            // string tag = Encoding.ASCII.GetString(data, offset[0], tagLen)
            var tagLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("ASCII")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldloc, tagLenLocal);
            il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetString", [typeof(byte[]), _types.Int32, _types.Int32])!);
            il.Emit(OpCodes.Stloc, tagLocal);

            // offset[0] += tagLen
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldloc, tagLenLocal);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stelem_I4);

            // int valueLen = rdlength - 2 - tagLen
            var valueLenLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_3); // rdlength
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Ldloc, tagLenLocal);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, valueLenLocal);

            // string value = Encoding.UTF8.GetString(data, offset[0], valueLen)
            var valueLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Ldloc, valueLenLocal);
            il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetString", [typeof(byte[]), _types.Int32, _types.Int32])!);
            il.Emit(OpCodes.Stloc, valueLocal);

            // Build dictionary { critical: (double)(flags & 0x80), [tag]: value }
            il.Emit(OpCodes.Newobj, dictCtor);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "critical");
            // Node reports the whole flags byte, not just the 0x80 bit (#1073)
            il.Emit(OpCodes.Ldloc, flagsLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc, tagLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Stloc, resultLocal);

            EmitSetOffsetToRdataEnd(il);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === TypeNAPTR (35): order(2) + preference(2) + 3xCharString + ReadName -> Dictionary ===
        il.MarkLabel(typeNAPTRLabel);
        {
            var orderLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt16);
            il.Emit(OpCodes.Stloc, orderLocal);

            var prefLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadUInt16);
            il.Emit(OpCodes.Stloc, prefLocal);

            var naptrFlagsLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadCharString);
            il.Emit(OpCodes.Stloc, naptrFlagsLocal);

            var serviceLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadCharString);
            il.Emit(OpCodes.Stloc, serviceLocal);

            var regexpLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadCharString);
            il.Emit(OpCodes.Stloc, regexpLocal);

            var replacementLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DnsReadName);
            il.Emit(OpCodes.Stloc, replacementLocal);

            il.Emit(OpCodes.Newobj, dictCtor);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "flags");
            il.Emit(OpCodes.Ldloc, naptrFlagsLocal);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "service");
            il.Emit(OpCodes.Ldloc, serviceLocal);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "regexp");
            il.Emit(OpCodes.Ldloc, regexpLocal);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "replacement");
            il.Emit(OpCodes.Ldloc, replacementLocal);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "order");
            il.Emit(OpCodes.Ldloc, orderLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "preference");
            il.Emit(OpCodes.Ldloc, prefLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, dictAdd);
            il.Emit(OpCodes.Stloc, resultLocal);

            EmitSetOffsetToRdataEnd(il);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // === Default: skip RDATA, return null ===
        il.MarkLabel(defaultLabel);
        EmitSetOffsetAndReturnNull(il, returnLabel, resultLocal);

        // === Return ===
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: emits offset[0] = rdataStart + rdlength (using arg1, arg4, arg3).
    /// </summary>
    private static void EmitSetOffsetToRdataEnd(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_1); // offset (int[])
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg, 4); // rdataStart
        il.Emit(OpCodes.Ldarg_3); // rdlength
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);
    }

    /// <summary>
    /// Helper: emits offset[0] = rdataStart + rdlength, then result = null, branch to return.
    /// </summary>
    private static void EmitSetOffsetAndReturnNull(ILGenerator il, Label returnLabel, LocalBuilder resultLocal)
    {
        EmitSetOffsetToRdataEnd(il);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, returnLabel);
    }

    /// <summary>
    /// Emits DnsParseResponse: parses a full DNS response packet.
    /// Signature: object DnsParseResponse(byte[] data, int queryType, string hostname)
    /// </summary>
    private void EmitDnsParseResponseMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsParseResponse",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [typeof(byte[]), _types.Int32, _types.String]);
        runtime.DnsParseResponse = method;

        var il = method.GetILGenerator();

        var resultLocal = il.DeclareLocal(_types.Object);    // 0
        var returnLabel = il.DefineLabel();

        // if (data.Length < 12) throw
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, 12);
        var dataOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, dataOkLabel);

        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.resolve EAI_FAIL ");
        il.Emit(OpCodes.Ldarg_2); // hostname
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(dataOkLabel);

        // int rcode = data[3] & 0x0F
        var rcodeLocal = il.DeclareLocal(_types.Int32); // 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x0F);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Stloc, rcodeLocal);

        // if (rcode != 0) throw with appropriate error code
        il.Emit(OpCodes.Ldloc, rcodeLocal);
        var rcodeOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, rcodeOkLabel);

        // Switch on rcode for error string
        var rcode1Label = il.DefineLabel();
        var rcode2Label = il.DefineLabel();
        var rcode3Label = il.DefineLabel();
        var rcode4Label = il.DefineLabel();
        var rcode5Label = il.DefineLabel();
        var rcodeDefaultLabel = il.DefineLabel();
        var throwRcodeLabel = il.DefineLabel();

        var errorCodeLocal = il.DeclareLocal(_types.String); // 2

        il.Emit(OpCodes.Ldloc, rcodeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, rcode1Label);
        il.Emit(OpCodes.Ldloc, rcodeLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, rcode2Label);
        il.Emit(OpCodes.Ldloc, rcodeLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Beq, rcode3Label);
        il.Emit(OpCodes.Ldloc, rcodeLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Beq, rcode4Label);
        il.Emit(OpCodes.Ldloc, rcodeLocal);
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Beq, rcode5Label);
        il.Emit(OpCodes.Br, rcodeDefaultLabel);

        il.MarkLabel(rcode1Label);
        il.Emit(OpCodes.Ldstr, "EFORMERR");
        il.Emit(OpCodes.Stloc, errorCodeLocal);
        il.Emit(OpCodes.Br, throwRcodeLabel);

        il.MarkLabel(rcode2Label);
        il.Emit(OpCodes.Ldstr, "ESERVFAIL");
        il.Emit(OpCodes.Stloc, errorCodeLocal);
        il.Emit(OpCodes.Br, throwRcodeLabel);

        il.MarkLabel(rcode3Label);
        il.Emit(OpCodes.Ldstr, "ENOTFOUND");
        il.Emit(OpCodes.Stloc, errorCodeLocal);
        il.Emit(OpCodes.Br, throwRcodeLabel);

        il.MarkLabel(rcode4Label);
        il.Emit(OpCodes.Ldstr, "ENOTIMP");
        il.Emit(OpCodes.Stloc, errorCodeLocal);
        il.Emit(OpCodes.Br, throwRcodeLabel);

        il.MarkLabel(rcode5Label);
        il.Emit(OpCodes.Ldstr, "EREFUSED");
        il.Emit(OpCodes.Stloc, errorCodeLocal);
        il.Emit(OpCodes.Br, throwRcodeLabel);

        il.MarkLabel(rcodeDefaultLabel);
        il.Emit(OpCodes.Ldstr, "EAI_FAIL");
        il.Emit(OpCodes.Stloc, errorCodeLocal);

        // throw new Exception("Runtime Error: dns.resolve {errorCode} {hostname}")
        il.MarkLabel(throwRcodeLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.resolve ");
        il.Emit(OpCodes.Ldloc, errorCodeLocal);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldarg_2); // hostname
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(rcodeOkLabel);

        // int ancount = (data[6] << 8) | data[7]
        var ancountLocal = il.DeclareLocal(_types.Int32); // 3
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, ancountLocal);

        // int[] offset = new int[] { 12 }
        var offsetLocal = il.DeclareLocal(typeof(int[])); // 4
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Int32);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, 12);
        il.Emit(OpCodes.Stelem_I4);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Skip question section: qdcount = (data[4] << 8) | data[5]
        var qdcountLocal = il.DeclareLocal(_types.Int32); // 5
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, qdcountLocal);

        // for (int q = 0; q < qdcount; q++) { SkipName(data, offset); offset[0] += 4; }
        var qLocal = il.DeclareLocal(_types.Int32); // 6
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, qLocal);
        var qLoopStart = il.DefineLabel();
        var qLoopCond = il.DefineLabel();
        il.Emit(OpCodes.Br, qLoopCond);

        il.MarkLabel(qLoopStart);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Call, runtime.DnsSkipName);
        // offset[0] += 4 (QTYPE + QCLASS)
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);
        il.Emit(OpCodes.Ldloc, qLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, qLocal);
        il.MarkLabel(qLoopCond);
        il.Emit(OpCodes.Ldloc, qLocal);
        il.Emit(OpCodes.Ldloc, qdcountLocal);
        il.Emit(OpCodes.Blt, qLoopStart);

        // var results = new List<object?>()
        var resultsLocal = il.DeclareLocal(_types.ListOfObject); // 7
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultsLocal);

        // for (int i = 0; i < ancount; i++)
        var iLocal = il.DeclareLocal(_types.Int32); // 8
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var ansLoopStart = il.DefineLabel();
        var ansLoopCond = il.DefineLabel();
        il.Emit(OpCodes.Br, ansLoopCond);

        il.MarkLabel(ansLoopStart);

        // SkipName(data, offset)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Call, runtime.DnsSkipName);

        // int type = (data[offset[0]] << 8) | data[offset[0] + 1]
        var typeLocal = il.DeclareLocal(_types.Int32); // 9
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, typeLocal);

        // offset[0] += 2 (TYPE)
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);

        // offset[0] += 2 (CLASS)
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);

        // offset[0] += 4 (TTL)
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);

        // int rdlength = (data[offset[0]] << 8) | data[offset[0] + 1]
        var rdlengthLocal = il.DeclareLocal(_types.Int32); // 10
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, rdlengthLocal);

        // offset[0] += 2 (RDLENGTH)
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);

        // int rdataStart = offset[0]
        var rdataStartLocal = il.DeclareLocal(_types.Int32); // 11
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Stloc, rdataStartLocal);

        // if (type == queryType)
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldarg_1); // queryType
        var skipRecordLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, skipRecordLabel);

        // var record = DnsParseRecord(data, offset, type, rdlength, rdataStart)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldloc, rdlengthLocal);
        il.Emit(OpCodes.Ldloc, rdataStartLocal);
        il.Emit(OpCodes.Call, runtime.DnsParseRecord);
        var recordLocal = il.DeclareLocal(_types.Object); // 12
        il.Emit(OpCodes.Stloc, recordLocal);

        // if (record != null) results.Add(record)
        il.Emit(OpCodes.Ldloc, recordLocal);
        var ansLoopContinue = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, ansLoopContinue);
        il.Emit(OpCodes.Ldloc, resultsLocal);
        il.Emit(OpCodes.Ldloc, recordLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);
        il.Emit(OpCodes.Br, ansLoopContinue);

        // else: offset[0] += rdlength (skip non-matching record)
        il.MarkLabel(skipRecordLabel);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldloc, rdlengthLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);

        il.MarkLabel(ansLoopContinue);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(ansLoopCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, ancountLocal);
        il.Emit(OpCodes.Blt, ansLoopStart);

        // if (queryType == TypeSOA (6))
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_6);
        var notSoaLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notSoaLabel);

        // SOA: if (results.Count == 0) throw ENODATA
        il.Emit(OpCodes.Ldloc, resultsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        var soaHasResultLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, soaHasResultLabel);

        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.resolveSoa ENODATA ");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(soaHasResultLabel);
        // return results[0]
        il.Emit(OpCodes.Ldloc, resultsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, returnLabel);

        il.MarkLabel(notSoaLabel);

        // if (results.Count == 0) throw ENODATA with method-specific prefix
        il.Emit(OpCodes.Ldloc, resultsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        var hasResultsLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasResultsLabel);

        // Generic ENODATA error — use "dns.resolve" as method name
        il.Emit(OpCodes.Ldstr, "Runtime Error: dns.resolve ENODATA ");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(hasResultsLabel);

        // return results (as List<object?>)
        il.Emit(OpCodes.Ldloc, resultsLocal);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DnsConvertList: converts a List&lt;object?&gt; of raw DNS results to $Array/$Object.
    /// Inner List elements become $Array, inner Dictionary elements become $Object, strings stay as-is.
    /// </summary>
    private void EmitDnsConvertList(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsConvertList",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]);
        runtime.DnsConvertList = method;

        var il = method.GetILGenerator();

        // Create output List<object?>
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        var outListLocal = il.DeclareLocal(_types.ListOfObject); // 0
        il.Emit(OpCodes.Stloc, outListLocal);

        // for (int i = 0; i < input.Count; i++)
        var indexLocal = il.DeclareLocal(typeof(int)); // 1
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopCond = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopStart);

        // var item = input[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        var itemLocal = il.DeclareLocal(_types.Object); // 2
        il.Emit(OpCodes.Stloc, itemLocal);

        // if (item is Dictionary<string, object?>) -> CreateObject
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var notInnerDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notInnerDictLabel);

        il.Emit(OpCodes.Ldloc, outListLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);
        var continueLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, continueLabel);

        il.MarkLabel(notInnerDictLabel);

        // if (item is List<object?>) -> new $Array
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        var notInnerListLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notInnerListLabel);

        il.Emit(OpCodes.Ldloc, outListLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);
        il.Emit(OpCodes.Br, continueLabel);

        il.MarkLabel(notInnerListLabel);

        // else: add as-is (string, etc.)
        il.Emit(OpCodes.Ldloc, outListLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

        il.MarkLabel(continueLabel);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Blt, loopStart);

        // return new $Array(outList)
        il.Emit(OpCodes.Ldloc, outListLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits async DNS resolution wrapper methods: resolve, resolve4, resolve6, reverse.
    /// Each calls the sync DnsLookup method and invokes the callback with (null, result) or (error, null).
    /// </summary>
    private void EmitDnsAsyncResolveWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // resolve4(hostname, callback) -> calls DnsLookup with family=4, returns array of IPs
        EmitDnsResolveByFamily(typeBuilder, runtime, "resolve4", 4);

        // resolve6(hostname, callback) -> calls DnsLookup with family=6, returns array of IPs
        EmitDnsResolveByFamily(typeBuilder, runtime, "resolve6", 6);

        // resolve(hostname, rrtype_or_callback, callback?) -> calls DnsLookup
        EmitDnsResolveWrapper(typeBuilder, runtime);

        // reverse(ip, callback) -> reverse DNS lookup
        EmitDnsReverseWrapper(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits resolve4/resolve6: hostname + callback -> calls DnsLookup with fixed family, extracts addresses array.
    /// </summary>
    private void EmitDnsResolveByFamily(TypeBuilder typeBuilder, EmittedRuntime runtime, string methodName, int family)
    {
        // DnsWrapper_resolve4(hostname, callback)
        var method = typeBuilder.DefineMethod(
            "DnsWrapper_" + methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);
        var endLabel = il.DefineLabel();

        il.BeginExceptionBlock();

        // resultLocal = DnsResolveRecord(hostname, "A"|"AAAA") — A/AAAA via the emitted
        // DNS wire protocol (honors SHARPTS_DNS_SERVER, Node/c-ares semantics — no
        // hosts-file lookup), mirroring the interpreter. Returns a $Array of IP strings.
        il.Emit(OpCodes.Ldarg_0); // hostname (object)
        il.Emit(OpCodes.Ldstr, family == 4 ? "A" : "AAAA");
        il.Emit(OpCodes.Call, runtime.DnsResolveRecord);
        il.Emit(OpCodes.Stloc, resultLocal);

        // callback.Invoke([null, result])
        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        // catch
        il.BeginCatchBlock(typeof(Exception));
        var exLocal = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLocal);

        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(Exception), "get_Message"));
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("dns", methodName, method);
    }

    /// <summary>
    /// Emits resolve(hostname, rrtype_or_callback, callback?):
    /// If 2 args: resolve(hostname, callback) with default rrtype="A"
    /// If 3 args: resolve(hostname, rrtype, callback)
    /// Routes to DnsResolveRecord for non-A/AAAA types, uses GetHostEntry for A/AAAA.
    /// </summary>
    private void EmitDnsResolveWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsWrapper_resolve",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var callbackLocal = il.DeclareLocal(_types.Object);
        var rrtypeLocal = il.DeclareLocal(_types.Object); // rrtype or null
        var endLabel = il.DefineLabel();

        // If arg2 != null, callback=arg2, rrtype=arg1 (3-arg form)
        // If arg2 == null, callback=arg1, rrtype=null (2-arg form, default "A")
        var threeArgLabel = il.DefineLabel();
        var afterCallbackLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, threeArgLabel);

        // 2-arg form: callback=arg1, rrtype=null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, rrtypeLocal);
        il.Emit(OpCodes.Br, afterCallbackLabel);

        il.MarkLabel(threeArgLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, rrtypeLocal);

        il.MarkLabel(afterCallbackLabel);

        // Check if rrtype is a non-A/AAAA string -> route to DnsResolveRecord
        var useGetHostEntryLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rrtypeLocal);
        il.Emit(OpCodes.Brfalse, useGetHostEntryLabel); // null -> default A

        // Check if rrtype is a string
        il.Emit(OpCodes.Ldloc, rrtypeLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, useGetHostEntryLabel); // not a string -> default A

        // Convert to upper and check
        il.Emit(OpCodes.Ldloc, rrtypeLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "ToUpperInvariant"));
        var upperRrtypeLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, upperRrtypeLocal);

        var strEquals = typeof(string).GetMethod("Equals", [typeof(string)])!;

        // If "A" or "AAAA" -> use GetHostEntry
        il.Emit(OpCodes.Ldloc, upperRrtypeLocal);
        il.Emit(OpCodes.Ldstr, "A");
        il.Emit(OpCodes.Callvirt, strEquals);
        il.Emit(OpCodes.Brtrue, useGetHostEntryLabel);

        il.Emit(OpCodes.Ldloc, upperRrtypeLocal);
        il.Emit(OpCodes.Ldstr, "AAAA");
        il.Emit(OpCodes.Callvirt, strEquals);
        il.Emit(OpCodes.Brtrue, useGetHostEntryLabel);

        // Non-A/AAAA rrtype -> use DnsResolveRecord
        il.BeginExceptionBlock();

        il.Emit(OpCodes.Ldarg_0); // hostname
        il.Emit(OpCodes.Ldloc, rrtypeLocal); // rrtype
        il.Emit(OpCodes.Call, runtime.DnsResolveRecord);
        var recordResultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, recordResultLocal);

        // callback(null, result)
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, recordResultLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.BeginCatchBlock(typeof(Exception));
        var exLocal1 = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLocal1);

        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exLocal1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(Exception), "get_Message"));
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.EndExceptionBlock();
        il.Emit(OpCodes.Br, endLabel);

        // === GetHostEntry path (A/AAAA or default) ===
        il.MarkLabel(useGetHostEntryLabel);

        il.BeginExceptionBlock();

        // resultLocal = DnsResolveRecord(hostname, rrtype ?? "A") — A/AAAA and the
        // default rrtype route through the emitted DNS wire protocol (honors
        // SHARPTS_DNS_SERVER, Node/c-ares semantics), mirroring the interpreter.
        // Returns a $Array of IP strings.
        il.Emit(OpCodes.Ldarg_0); // hostname (object)
        il.Emit(OpCodes.Ldloc, rrtypeLocal);
        il.Emit(OpCodes.Isinst, _types.String); // rrtype string, or null
        il.Emit(OpCodes.Dup);
        var haveRrtypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, haveRrtypeLabel);
        il.Emit(OpCodes.Pop);          // drop null
        il.Emit(OpCodes.Ldstr, "A");   // default rrtype
        il.MarkLabel(haveRrtypeLabel);
        il.Emit(OpCodes.Call, runtime.DnsResolveRecord);
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);

        // callback(null, result)
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.BeginCatchBlock(typeof(Exception));
        var exLocal = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLocal);

        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(Exception), "get_Message"));
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("dns", "resolve", method);
    }

    /// <summary>
    /// Emits reverse(ip, callback): reverse DNS lookup.
    /// </summary>
    private void EmitDnsReverseWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsWrapper_reverse",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);
        var endLabel = il.DefineLabel();

        il.BeginExceptionBlock();

        // Parse IP address
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [typeof(string)])!);
        var ipLocal = il.DeclareLocal(typeof(IPAddress));
        il.Emit(OpCodes.Stloc, ipLocal);

        // Dns.GetHostEntry(ip) -> IPHostEntry
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Call, typeof(Dns).GetMethod("GetHostEntry", [typeof(IPAddress)])!);
        var hostEntryLocal = il.DeclareLocal(typeof(IPHostEntry));
        il.Emit(OpCodes.Stloc, hostEntryLocal);

        // Build array with hostname
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, hostEntryLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPHostEntry).GetProperty("HostName")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // callback(null, result)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.BeginCatchBlock(typeof(Exception));
        var exLocal = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(Exception), "get_Message"));
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("dns", "reverse", method);
    }

    /// <summary>
    /// Emits async callback wrappers for DNS record type methods:
    /// resolveMx, resolveTxt, resolveSrv, resolveCname, resolveNs, resolveSoa, resolvePtr, resolveCaa, resolveNaptr.
    /// Each takes (hostname, callback) and calls DnsResolveRecord(hostname, rrtype).
    /// </summary>
    private void EmitDnsRecordTypeWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var recordTypes = new[]
        {
            ("resolveMx", "MX"),
            ("resolveTxt", "TXT"),
            ("resolveSrv", "SRV"),
            ("resolveCname", "CNAME"),
            ("resolveNs", "NS"),
            ("resolveSoa", "SOA"),
            ("resolvePtr", "PTR"),
            ("resolveCaa", "CAA"),
            ("resolveNaptr", "NAPTR")
        };

        foreach (var (methodName, rrtype) in recordTypes)
        {
            EmitDnsRecordTypeWrapper(typeBuilder, runtime, methodName, rrtype);
        }
    }

    /// <summary>
    /// Emits a single DNS record type callback wrapper.
    /// Runs DNS resolution on a ThreadPool thread and schedules the callback on the EventLoop,
    /// matching the interpreter's async behavior and preventing main-thread blocking.
    /// </summary>
    private void EmitDnsRecordTypeWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime, string methodName, string rrtype)
    {
        var method = typeBuilder.DefineMethod(
            "DnsWrapper_" + methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);

        // Emit the async closure class for this record type
        var closureType = EmitDnsAsyncClosure(typeBuilder, runtime, methodName, rrtype);

        var il = method.GetILGenerator();

        // EventLoop.Ref() — keep event loop alive during async DNS
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // ThreadPool.QueueUserWorkItem(new $DnsAsyncClosure_xxx(hostname, callback).Worker)
        il.Emit(OpCodes.Ldarg_0); // hostname
        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Newobj, closureType.ctor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldftn, closureType.worker);
        il.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Pop); // pop the dup'd closure

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("dns", methodName, method);
    }

    /// <summary>
    /// Emits a closure class for async DNS resolution.
    /// Has Worker(object) for ThreadPool and Callback() for EventLoop dispatch.
    /// </summary>
    private (ConstructorBuilder ctor, MethodBuilder worker) EmitDnsAsyncClosure(
        TypeBuilder parentType, EmittedRuntime runtime, string methodName, string rrtype)
    {
        var closureType = ((ModuleBuilder)parentType.Module).DefineType(
            "$DnsAsyncClosure_" + methodName,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object));

        var hostnameField = closureType.DefineField("_hostname", _types.Object, FieldAttributes.Private);
        var callbackField = closureType.DefineField("_callback", _types.Object, FieldAttributes.Private);
        var resultField = closureType.DefineField("_result", _types.Object, FieldAttributes.Private);
        var errorField = closureType.DefineField("_error", _types.String, FieldAttributes.Private);

        // Constructor: (hostname, callback)
        var ctor = closureType.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [_types.Object, _types.Object]);
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, hostnameField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, callbackField);
            il.Emit(OpCodes.Ret);
        }

        // Callback() — runs on event loop, invokes the JS callback
        var callbackMethod = closureType.DefineMethod(
            "Callback", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        {
            var il = callbackMethod.GetILGenerator();

            // if (_error != null) → error path
            var errorPath = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, errorField);
            il.Emit(OpCodes.Brtrue, errorPath);

            // Success: callback.Invoke([null, _result])
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, callbackField);
            il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resultField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, done);

            // Error: callback.Invoke([_error, null])
            il.MarkLabel(errorPath);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, callbackField);
            il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, errorField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(done);

            // EventLoop.Unref()
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Call, runtime.EventLoopUnref);

            il.Emit(OpCodes.Ret);
        }

        // Worker(object state) — runs on ThreadPool
        var worker = closureType.DefineMethod(
            "Worker", MethodAttributes.Public, typeof(void), [_types.Object]);
        {
            var il = worker.GetILGenerator();
            var afterTry = il.DefineLabel();

            il.BeginExceptionBlock();

            // _result = DnsResolveRecord((string)_hostname, rrtype)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, hostnameField);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Ldstr, rrtype);
            il.Emit(OpCodes.Call, runtime.DnsResolveRecord);
            il.Emit(OpCodes.Stfld, resultField);
            il.Emit(OpCodes.Leave, afterTry);

            il.BeginCatchBlock(typeof(Exception));
            // _error = ex.Message
            var exLocal = il.DeclareLocal(typeof(Exception));
            il.Emit(OpCodes.Stloc, exLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, exLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(Exception), "get_Message"));
            il.Emit(OpCodes.Stfld, errorField);
            il.Emit(OpCodes.Leave, afterTry);

            il.EndExceptionBlock();
            il.MarkLabel(afterTry);

            // EventLoop.Schedule(new Action(this.Callback))
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldftn, callbackMethod);
            il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Call, runtime.EventLoopSchedule);

            il.Emit(OpCodes.Ret);
        }

        closureType.CreateType();
        return (ctor, worker);
    }

    /// <summary>
    /// Emits DnsResolverFactory stub — actual implementation in RuntimeEmitter.DnsResolver.cs.
    /// </summary>
    private void EmitDnsResolverFactory(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitDnsResolverFactoryMethod(typeBuilder, runtime);
    }
}
