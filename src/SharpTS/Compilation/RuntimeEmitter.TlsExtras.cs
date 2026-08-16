using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography.X509Certificates;

namespace SharpTS.Compilation;

/// <summary>
/// Emits tls.getCiphers() and tls.rootCertificates as pure-BCL IL.
/// getCiphers returns the exact same list as interp (read from
/// TlsModuleInterpreter.StandardCiphers, so interp == compiled by construction);
/// rootCertificates enumerates the platform Root trust store like interp.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: public static object TlsGetCiphers() — a List&lt;object&gt; of lowercased cipher names.
    /// </summary>
    private void EmitTlsGetCiphers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TlsGetCiphers",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.TlsGetCiphers = method;
        runtime.RegisterBuiltInModuleMethod("tls", "getCiphers", method);

        var il = method.GetILGenerator();
        var listType = _types.ListOfObject;
        var listAdd = _types.GetMethod(listType, "Add")!;

        var listLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(listType));
        il.Emit(OpCodes.Stloc, listLocal);

        // Shared source of truth — identical to interp's getCiphers().
        foreach (var cipher in SharpTS.Runtime.BuiltIns.Modules.Interpreter.TlsModuleInterpreter.StandardCiphers)
        {
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldstr, cipher);
            il.Emit(OpCodes.Callvirt, listAdd);
        }

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object TlsRootCertificates() — a List&lt;object&gt; of PEM root certs
    /// enumerated from the platform Root store (CurrentUser + LocalMachine).
    /// </summary>
    private void EmitTlsRootCertificates(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TlsRootCertificates",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.TlsRootCertificates = method;

        var il = method.GetILGenerator();
        var listType = _types.ListOfObject;
        var listAdd = _types.GetMethod(listType, "Add")!;

        var listLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(listType));
        il.Emit(OpCodes.Stloc, listLocal);

        EmitEnumerateStore(il, listLocal, listAdd, StoreLocation.CurrentUser);
        EmitEnumerateStore(il, listLocal, listAdd, StoreLocation.LocalMachine);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: try { using var store = new X509Store(Root, loc); store.Open(ReadOnly);
    /// foreach (var c in store.Certificates) list.Add(c.ExportCertificatePem()); } catch { }
    /// </summary>
    private void EmitEnumerateStore(ILGenerator il, LocalBuilder listLocal, MethodInfo listAdd, StoreLocation loc)
    {
        var collectionType = typeof(X509Certificate2Collection);
        var enumeratorType = typeof(X509Certificate2Enumerator);

        var storeLocal = il.DeclareLocal(typeof(X509Store));
        var enumLocal = il.DeclareLocal(enumeratorType);

        il.BeginExceptionBlock();

        // store = new X509Store(StoreName.Root, loc)
        il.Emit(OpCodes.Ldc_I4, (int)StoreName.Root);
        il.Emit(OpCodes.Ldc_I4, (int)loc);
        il.Emit(OpCodes.Newobj, typeof(X509Store).GetConstructor([typeof(StoreName), typeof(StoreLocation)])!);
        il.Emit(OpCodes.Stloc, storeLocal);
        // store.Open(OpenFlags.ReadOnly)
        il.Emit(OpCodes.Ldloc, storeLocal);
        il.Emit(OpCodes.Ldc_I4, (int)OpenFlags.ReadOnly);
        il.Emit(OpCodes.Callvirt, typeof(X509Store).GetMethod("Open", [typeof(OpenFlags)])!);

        // var e = store.Certificates.GetEnumerator();
        il.Emit(OpCodes.Ldloc, storeLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509Store).GetProperty("Certificates")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, collectionType.GetMethod("GetEnumerator", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, enumLocal);

        var top = il.DefineLabel();
        var chk = il.DefineLabel();
        il.Emit(OpCodes.Br, chk);
        il.MarkLabel(top);
        // list.Add(e.Current.ExportCertificatePem())
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetMethod("ExportCertificatePem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, listAdd);
        il.MarkLabel(chk);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brtrue, top);

        // store.Dispose() (using)
        il.Emit(OpCodes.Ldloc, storeLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509Store).GetMethod("Dispose", Type.EmptyTypes)!);

        var done = il.DefineLabel();
        il.Emit(OpCodes.Leave, done);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, done);
        il.EndExceptionBlock();
        il.MarkLabel(done);
    }
}
