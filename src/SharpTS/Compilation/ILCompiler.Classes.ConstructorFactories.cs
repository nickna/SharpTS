using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    /// <summary>Adapts a flat guest argument list to this declaration's canonical constructor.</summary>
    private void EmitDeclaredConstructorFactory(TypeBuilder owner, Stmt.Class declaration, ConstructorBuilder constructor)
    {
        bool abstractOwner = (owner.Attributes & TypeAttributes.Abstract) != 0;
        bool abstractReceiverFactory = abstractOwner && _usesRuntimeParents && !owner.IsGenericTypeDefinition
            && owner.BaseType is not { Name: "$Promise" }
            && !_classes.ErrorSubclasses.Contains(GetQualifiedClassDeclarationName(declaration))
            && !_classes.PromiseSubclasses.Contains(GetQualifiedClassDeclarationName(declaration))
            && SupportsReceiverInitialization(declaration);
        if (abstractOwner && !abstractReceiverFactory)
            return;

        string name = "$ConstructFromArguments";
        while (declaration.Methods.Any(method => method.Name.Lexeme == name))
            name += "_";
        var factory = owner.DefineMethod(name, MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [_types.ObjectArray, _types.Object]);
        owner.SetCustomAttribute(_runtime.DynamicConstruction.FactoryAttributeConstructor,
            CustomAttributeEncoder.Encode(_runtime.DynamicConstruction.FactoryAttributeConstructor, name));

        var parameters = constructor.GetParameters();
        var sourceParameters = ResolveConstructorFactorySourceParameters(owner, declaration);
        int restIndex = sourceParameters?.Select((parameter, index) => (parameter, index))
            .Where(entry => entry.parameter.IsRest).Select(entry => entry.index).DefaultIfEmpty(-1).First() ?? -1;
        bool arrayForwarder = declaration.Methods.All(method => method.Name.Lexeme != "constructor" || method.Body == null)
            && _classes.ArraySubclasses.Contains(GetQualifiedClassDeclarationName(declaration))
            && parameters.Length == 1 && parameters[0].ParameterType == _types.ObjectArray;

        var il = factory.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, factory);
        var emitter = new ILEmitter(ctx);
        if (abstractReceiverFactory || (UsesLiveSuperConstruction(declaration) && !owner.IsGenericTypeDefinition
            && !_classes.ErrorSubclasses.Contains(GetQualifiedClassDeclarationName(declaration))
            && !_classes.PromiseSubclasses.Contains(GetQualifiedClassDeclarationName(declaration))))
        {
            var nominalConstruction = il.DefineLabel();
            bool hasNominalPath = !abstractOwner
                && EmitNominalAbstractConstructorGuard(il, owner, declaration, nominalConstruction);
            // The adapter's completion key is independent of the receiver
            // returned by the dynamically selected superclass. An abstract CLR
            // template uses this adapter without allocating the abstract type.
            il.Emit(OpCodes.Ldtoken, owner);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _runtime.ClassDefinitions.InitializeReceiver);
            il.Emit(OpCodes.Ret);
            if (!hasNominalPath) return;
            il.MarkLabel(nominalConstruction);
        }
        bool publishesArguments = _functions.MethodsCapturingArguments.Contains(constructor);
        LocalBuilder? previousArguments = null;
        LocalBuilder? result = null;
        Label done = default;
        if (publishesArguments)
        {
            previousArguments = il.DeclareLocal(_types.ObjectArray);
            result = il.DeclareLocal(_types.Object);
            done = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, _runtime.Arguments.CurrentField);
            il.Emit(OpCodes.Stloc, previousArguments);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stsfld, _runtime.Arguments.CurrentField);
            il.BeginExceptionBlock();
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            if (arrayForwarder)
            {
                il.Emit(OpCodes.Ldarg_0);
                continue;
            }
            if (i == restIndex)
            {
                // Rest is determined by the source declaration, never by an array-like CLR slot.
                var rest = il.DeclareLocal(_runtime.ArrayStorage.Type);
                var index = il.DeclareLocal(_types.Int32);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Call, _types.GetMethod(typeof(Math), "Max", _types.Int32, _types.Int32));
                il.Emit(OpCodes.Newobj, _runtime.ArrayStorage.RestCtor);
                il.Emit(OpCodes.Stloc, rest);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Stloc, index);
                var loop = il.DefineLabel();
                var finished = il.DefineLabel();
                il.MarkLabel(loop);
                il.Emit(OpCodes.Ldloc, index);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Bge, finished);
                il.Emit(OpCodes.Ldloc, rest);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, index);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Call, _runtime.ArrayStorage.AppendRest);
                il.Emit(OpCodes.Ldloc, index);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, index);
                il.Emit(OpCodes.Br, loop);
                il.MarkLabel(finished);
                il.Emit(OpCodes.Ldloc, rest);
                continue;
            }

            var missing = il.DefineLabel();
            var ready = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ble, missing);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldelem_Ref);
            var slot = parameters[i].ParameterType;
            if (slot.IsValueType || slot.IsGenericParameter)
            {
                // Preserve Activator's supplied-null conversion for CLR value slots.
                // Optional/default slots are object and keep their guest null value.
                var supplied = il.DefineLabel();
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brtrue, supplied);
                il.Emit(OpCodes.Pop);
                EmitConstructorFactoryDefault(il, emitter, slot);
                il.Emit(OpCodes.Br, ready);
                il.MarkLabel(supplied);
            }
            if (slot == _types.Double)
                EmitConstructorFactoryDoubleConversion(il);
            else if (slot != _types.Object)
                il.Emit(slot.IsValueType || slot.IsGenericParameter ? OpCodes.Unbox_Any : OpCodes.Castclass, slot);
            il.Emit(OpCodes.Br, ready);
            il.MarkLabel(missing);
            EmitConstructorFactoryDefault(il, emitter, slot);
            il.MarkLabel(ready);
        }

        Type self = owner.IsGenericTypeDefinition
            ? EmitGenerics.MakeGenericType(owner, owner.GetGenericArguments()) : owner;
        var bodyConstructor = NewTargetConstructor(constructor);
        if (bodyConstructor != constructor) il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, self == owner ? bodyConstructor : EmitterTypeHelpers.ResolveConstructor(self, bodyConstructor));
        il.Emit(OpCodes.Call, _runtime.DynamicConstruction.ResolveClassReturn);
        if (!publishesArguments)
        {
            il.Emit(OpCodes.Ret);
            return;
        }
        il.Emit(OpCodes.Stloc, result!);
        il.Emit(OpCodes.Leave, done);
        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloc, previousArguments!);
        il.Emit(OpCodes.Stsfld, _runtime.Arguments.CurrentField);
        il.EndExceptionBlock();
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitConstructorFactoryDoubleConversion(ILGenerator il)
    {
        // Match the CLR binder's primitive widening to Double. In particular,
        // interop calls can return boxed Int32 rather than a guest boxed Double.
        // Strings, booleans, decimal and guest objects keep the strict unbox path.
        var converted = il.DefineLabel();
        Type[] numericSources = [_types.SByte, _types.Byte, _types.Int16, _types.UInt16,
            _types.Char, _types.Int32, _types.UInt32, _types.Int64, _types.UInt64, _types.Single];
        foreach (var source in numericSources)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Isinst, source);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Unbox_Any, source);
            if (source == _types.UInt32 || source == _types.UInt64)
                il.Emit(OpCodes.Conv_R_Un);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Br, converted);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.MarkLabel(converted);
    }

    private static void EmitConstructorFactoryDefault(ILGenerator il, ILEmitter emitter, Type slot)
    {
        if (!slot.IsGenericParameter)
        {
            emitter.EmitOmittedArgument(slot);
            return;
        }
        var value = il.DeclareLocal(slot);
        il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Initobj, slot);
        il.Emit(OpCodes.Ldloc, value);
    }

    private IReadOnlyList<Stmt.Parameter>? ResolveConstructorFactorySourceParameters(TypeBuilder owner, Stmt.Class declaration)
    {
        while (true)
        {
            var constructor = declaration.Methods.FirstOrDefault(method => method.Name.Lexeme == "constructor" && !method.IsStatic && method.Body != null);
            if (constructor != null) return constructor.Parameters;
            var parent = owner.BaseType;
            if (parent?.IsConstructedGenericType == true) parent = parent.GetGenericTypeDefinition();
            if (parent is not TypeBuilder builder) return null;
            var parentDeclaration = _classes.Declarations.FirstOrDefault(candidate =>
                _classes.DeclarationNames.TryGetValue(candidate, out var name)
                && _classes.Builders.TryGetValue(name, out var candidateOwner) && ReferenceEquals(candidateOwner, builder));
            if (parentDeclaration == null) return null;
            owner = builder;
            declaration = parentDeclaration;
        }
    }
}
