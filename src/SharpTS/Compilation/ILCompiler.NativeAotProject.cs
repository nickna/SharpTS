using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private bool _nativeAotProjectOutput;
    private NativePropertyLookupCatalog? _nativePropertyLookup;
    private NativeMethodLookupCatalog? _nativeMethodLookup;
    private NativeHasInMemberLookup? _nativeHasInMembers;
    private NativeReceiverInitializerLookup? _nativeReceiverInitializerLookup;
    private NativeOwnerClosure? _nativeOwnerClosure;
    internal NativeOwnerClosure? NativeOwnerClosures => _nativeOwnerClosure;
    internal NativeReceiverInitializerLookup? NativeReceiverInitializers => _nativeReceiverInitializerLookup;
    private NativeEvaluatedConstructorMetadata? _nativeEvaluatedConstructors;
    internal NativeEvaluatedConstructorMetadata? NativeEvaluatedConstructors => _nativeEvaluatedConstructors;
    private NativeConstructorActivationLookup? _nativeConstructorActivation;
    internal NativeConstructorActivationLookup? NativeConstructorActivation => _nativeConstructorActivation;
    internal NativeHasInMemberLookup? NativeHasInMembers => _nativeHasInMembers;
    internal NativeMethodLookupCatalog? NativeMethods => _nativeMethodLookup;
    internal NativePropertyLookupCatalog? NativeProperties => _nativePropertyLookup;
    private NativeClrAdmission? _nativeClrAdmission;
    private TypeBuilder? _nativeAotEntryFacade;
    private NativeClassInitializationEmission? _nativeClassInitialization;
    private FunctionEmissionPolicy _functionEmissionPolicy = FunctionEmissionPolicy.Full;
    private NativeSourceOwnerCatalog? _nativeSourceOwners;
    internal NativeSourceOwnerCatalog? NativeSourceOwners => _nativeSourceOwners;
    private NativeNumericRest4Binding? _nativeNumericRest4Binding;
    private NativeGenericMethodNormalization? _nativeGenericMethodNormalization;
    private NativeConstructedBodyLookup? _nativeConstructedBodies;
    private NativeConstructorFactoryLookup? _nativeConstructorFactories;
    internal NativeConstructorFactoryLookup? NativeConstructorFactories => _nativeConstructorFactories;
    internal NativeConstructedBodyLookup? NativeConstructedBodies => _nativeConstructedBodies;
    internal NativeGenericMethodNormalization? NativeGenericMethods => _nativeGenericMethodNormalization;
    private NativeUnionArgumentConversion? _nativeUnionArgumentConversion;
    internal NativeUnionArgumentConversion? NativeUnionArguments => _nativeUnionArgumentConversion;
    private NativeRuntimeClassInitialization? _nativeRuntimeClassInitialization;
    internal NativeRuntimeClassInitialization? NativeRuntimeInitializers => _nativeRuntimeClassInitialization;
    private NativeStaticMemberPresence? _nativeStaticMemberPresence;
    internal NativeStaticMemberPresence? NativeStaticMembers => _nativeStaticMemberPresence;
    private NativeStaticFieldLookup? _nativeStaticFieldLookup;
    internal NativeStaticFieldLookup? NativeStaticFields => _nativeStaticFieldLookup;
    private NativeConstructorMemberLookup? _nativeConstructorMembers;
    internal NativeConstructorMemberLookup? NativeConstructorMembers => _nativeConstructorMembers;
    private NativeDeletionLookup? _nativeDeletionLookup;
    internal NativeDeletionLookup? NativeDeletion => _nativeDeletionLookup;

    private NativeReceiverBinding? _nativeReceiverBinding;

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

    private void EnsureNativeClrAdmissionComplete()
    {
        if (!_nativeAotProjectOutput) return;
        (_nativeClrAdmission ?? throw new InvalidOperationException("Native CLR admission has not started source emission."))
            .ThrowIfIncompatible();
        if (_nativeConstructorFactories?.IsComplete != true)
            throw new InvalidOperationException("Native constructor-factory metadata has not completed source emission.");
        if (_nativeConstructedBodies?.IsComplete != true)
            throw new InvalidOperationException("Native constructor-body metadata has not completed source emission.");
        if (_nativePropertyLookup?.IsComplete != true)
            throw new InvalidOperationException("Native property metadata has not completed source emission.");
        if (_nativeHasInMembers?.IsComplete != true)
            throw new InvalidOperationException("Native in-operator member metadata has not completed source emission.");
        if (_nativeReceiverInitializerLookup?.IsComplete != true)
            throw new InvalidOperationException("Native receiver-initializer metadata has not completed source emission.");
        if (_nativeOwnerClosure?.IsComplete != true)
            throw new InvalidOperationException("Native owner-closure evidence has not completed source emission.");
        if (_nativeEvaluatedConstructors?.IsComplete != true)
            throw new InvalidOperationException("Native evaluated constructor metadata has not completed source emission.");
        if (_nativeConstructorActivation?.IsComplete != true)
            throw new InvalidOperationException("Native constructor-value metadata has not completed source emission.");
        if (_nativeStaticFieldLookup?.IsComplete != true)
            throw new InvalidOperationException("Native static field metadata has not completed source emission.");
        if (_nativeConstructorMembers?.IsComplete != true)
            throw new InvalidOperationException("Native constructor member metadata has not completed source emission.");
        if (_nativeMethodLookup?.IsComplete != true)
            throw new InvalidOperationException("Native method metadata has not completed source emission.");
        if (_nativeDeletionLookup?.IsComplete != true)
            throw new InvalidOperationException("Native deletion metadata has not completed source emission.");
    }

    private void RecordSourceRewrite(object source, object target)
    {
        _rawCallStorage!.RecordRewrite(source, target);
        _nativeClrAdmission?.RecordRewrite(source, target);
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
