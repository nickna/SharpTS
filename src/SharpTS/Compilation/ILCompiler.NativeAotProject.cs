using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private bool _nativeAotProjectOutput;
    private TypeBuilder? _nativeAotEntryFacade;
    private NativeClassInitializationEmission? _nativeClassInitialization;
    private FunctionEmissionPolicy _functionEmissionPolicy = FunctionEmissionPolicy.Full;
    private NativeSourceOwnerCatalog? _nativeSourceOwners;
    internal NativeSourceOwnerCatalog? NativeSourceOwners => _nativeSourceOwners;
    private NativeNumericRest4Binding? _nativeNumericRest4Binding;

    private NativeClassInitializationEmission? GetNativeClassInitialization() =>
        !_nativeAotProjectOutput || _programType is null ? null :
            _nativeClassInitialization ??= new(_programType, _types, _classes.GenericParameters);

    /// <summary>Enables the direct, C#-nameable guest entry used by native publish projects.</summary>
    internal void EnableNativeAotProjectOutput()
    {
        if (_outputTarget != OutputTarget.Dll || _hosted)
            throw new InvalidOperationException("Native guest projects require ordinary DLL output without the hosted ABI.");
        _nativeAotProjectOutput = true;
    }

    private void EmitNativeAotEntryFacade()
    {
        if (!_nativeAotProjectOutput) return;
        var entry = _entryPoint ?? throw new InvalidOperationException("The guest entry point has not been emitted.");
        if (entry.ReturnType != _types.Void || entry.GetParameters().Length != 0)
            throw new InvalidOperationException("The experimental native guest facade requires the ordinary top-level DLL entry.");
        _nativeAotEntryFacade = _moduleBuilder.DefineType("SharpTS.Generated.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        var run = _nativeAotEntryFacade.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32, Type.EmptyTypes);
        var il = run.GetILGenerator();
        il.Emit(OpCodes.Call, entry);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ExitCode"));
        il.Emit(OpCodes.Ret);
    }
}
