using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Helpers for resolving members on closed generic types when the type may or may not
/// contain a TypeBuilder among its generic arguments.
///
/// Background: <see cref="TypeBuilder.GetConstructor(Type, ConstructorInfo)"/> (and the
/// matching <c>GetMethod</c>/<c>GetField</c> overloads) require the closed generic's
/// argument list to include at least one TypeBuilder — they exist to bridge metadata
/// tokens for unbaked types referenced by IL. After <see cref="TypeBuilder.CreateType"/>:
///   - <see cref="System.Reflection.Emit.PersistedAssemblyBuilder"/> keeps types as
///     TypeBuilders until <c>Save()</c>, so <c>TypeBuilder.GetConstructor</c> still
///     applies for closed generics built from those types.
///   - The runtime <see cref="AssemblyBuilder"/> (used by tests' fast in-memory path)
///     bakes types eagerly to <c>RuntimeType</c>; <c>TypeBuilder.GetConstructor</c>
///     then throws <c>'type' must be or must contain a TypeBuilder</c>. Plain
///     <see cref="Type.GetConstructor(Type[])"/> is the right call there.
///
/// This helper auto-detects which path to take so emitter sites stay mode-agnostic.
/// </summary>
internal static class EmitterTypeHelpers
{
    private const string EmittedGenericMetadataJustification =
        "The closed generic type and open member are paired by the emitter; the selected member is persisted as an emitted IL metadata reference. Rooting every member on generic arguments would retain unrelated application metadata.";

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = EmittedGenericMetadataJustification)]
    public static ConstructorInfo ResolveConstructor(Type closedGeneric, ConstructorInfo openCtor)
    {
        if (ContainsTypeBuilder(closedGeneric))
            return TypeBuilder.GetConstructor(closedGeneric, openCtor);

        var paramTypes = Array.ConvertAll(openCtor.GetParameters(), p => p.ParameterType);
        return closedGeneric.GetConstructor(paramTypes)
            ?? throw new InvalidOperationException(
                $"No matching constructor on {closedGeneric} for open ctor {openCtor.DeclaringType}::{openCtor}");
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = EmittedGenericMetadataJustification)]
    public static MethodInfo ResolveMethod(Type closedGeneric, MethodInfo openMethod)
    {
        if (ContainsTypeBuilder(closedGeneric))
            return TypeBuilder.GetMethod(closedGeneric, openMethod);

        // RuntimeType: walk the closed generic's methods and match by name + signature
        // metadata tokens (parameter & return types resolved against the closed generic).
        var openParams = openMethod.GetParameters();
        foreach (var m in closedGeneric.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (m.Name != openMethod.Name) continue;
            var mp = m.GetParameters();
            if (mp.Length != openParams.Length) continue;
            return m;
        }
        throw new InvalidOperationException(
            $"No matching method on {closedGeneric} for open method {openMethod.DeclaringType}::{openMethod.Name}");
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = EmittedGenericMetadataJustification)]
    public static FieldInfo ResolveField(Type closedGeneric, FieldInfo openField)
    {
        if (ContainsTypeBuilder(closedGeneric))
            return TypeBuilder.GetField(closedGeneric, openField);

        return closedGeneric.GetField(openField.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"No matching field on {closedGeneric} for open field {openField.DeclaringType}::{openField.Name}");
    }

    /// <summary>
    /// Resolves a field token for IL emitted inside methods of the field's own declaring type.
    /// ECMA-335 (II.9.4) requires members of a generic type to be referenced through the
    /// instantiated form (e.g. <c>Stack&lt;!T&gt;::__Items</c>) rather than a raw token whose
    /// parent is the open TypeDef. Non-generic declaring types pass through unchanged.
    /// </summary>
    public static FieldInfo SelfFieldReference(FieldInfo field)
    {
        if (field.DeclaringType is TypeBuilder tb && tb.IsGenericTypeDefinition)
            return TypeBuilder.GetField(EmitGenerics.MakeGenericType(tb, tb.GetGenericArguments()), field);
        return field;
    }

    /// <summary>
    /// Resolves a method token for IL emitted inside methods of the method's own declaring type.
    /// Same ECMA-335 (II.9.4) rule as <see cref="SelfFieldReference"/>: members of a generic
    /// type must be referenced through the instantiated form (e.g. <c>Stack&lt;!T&gt;::get_Items</c>).
    /// Non-generic declaring types pass through unchanged.
    /// </summary>
    public static MethodInfo SelfMethodReference(MethodInfo method)
    {
        if (method.DeclaringType is TypeBuilder tb && tb.IsGenericTypeDefinition)
            return TypeBuilder.GetMethod(EmitGenerics.MakeGenericType(tb, tb.GetGenericArguments()), method);
        return method;
    }

    /// <summary>
    /// Resolves the cast type and call target for direct (compile-time) dispatch of
    /// <paramref name="method"/> on an instance statically typed as <paramref name="receiverClass"/>.
    ///
    /// A raw TypeDef token for an open generic class (e.g. <c>castclass Stack</c>) cannot be
    /// loaded by the CLR — resolving it at JIT time throws <c>TypeLoadException: Could not load
    /// type 'Stack'</c> (ECMA-335 II.9.4 requires the instantiated form). The instantiated
    /// self-form <c>Stack&lt;!T&gt;</c> is only expressible in IL bodies that live on that same
    /// generic type, where <c>!T</c> is in scope.
    ///
    /// Returns false when no valid token exists for the emission context — the caller must fall
    /// back to runtime (dynamic) dispatch, which handles generic instances correctly.
    /// </summary>
    /// <param name="receiverClass">The class TypeBuilder the receiver is statically typed as.</param>
    /// <param name="method">The method/accessor to invoke (may be declared on a generic base).</param>
    /// <param name="emittingType">The TypeBuilder whose method body is currently being emitted,
    /// or null when emitting into a function/state-machine/display-class body.</param>
    public static bool TryResolveInstanceDispatch(
        TypeBuilder receiverClass, MethodInfo method, Type? emittingType,
        out Type castType, out MethodInfo callTarget)
    {
        castType = receiverClass;
        callTarget = method;

        if (receiverClass.IsGenericTypeDefinition)
        {
            // Stack<!T> is only meaningful inside Stack<T>'s own method bodies.
            if (!ReferenceEquals(emittingType, receiverClass))
                return false;
            castType = EmitGenerics.MakeGenericType(receiverClass, receiverClass.GetGenericArguments());
        }

        // Methods declared on a generic (possibly base) class need a member reference
        // through the instantiation found on the receiver's inheritance chain.
        if (method.DeclaringType is TypeBuilder declType && declType.IsGenericTypeDefinition)
        {
            if (ReferenceEquals(declType, receiverClass))
            {
                callTarget = TypeBuilder.GetMethod(castType, method);
                return true;
            }

            return TryResolveOnBaseChain(receiverClass, declType, method, emittingType, out callTarget);
        }

        return true;
    }

    /// <summary>
    /// Resolves a call target for a method declared on a generic base class of
    /// <paramref name="fromClass"/>, referencing it through the instantiated base
    /// (e.g. <c>Base&lt;float64&gt;::count</c>) found on the inheritance chain.
    /// A raw MethodDef token on the open base is not executable — invoking it throws
    /// <c>InvalidOperationException: ... not fully instantiated</c>. Used for both
    /// inherited-member dispatch and <c>super.method()</c> calls (#178).
    /// Returns false when no expressible token exists for the emission context.
    /// </summary>
    public static bool TryResolveOnBaseChain(
        TypeBuilder fromClass, TypeBuilder declType, MethodInfo method, Type? emittingType,
        out MethodInfo callTarget)
    {
        callTarget = method;
        for (Type? t = fromClass.BaseType; t != null; t = t.BaseType)
        {
            if (t.IsGenericType && ReferenceEquals(t.GetGenericTypeDefinition(), declType))
            {
                // An instantiation written in terms of fromClass's own type parameters
                // (e.g. Base<!T>) is only expressible inside fromClass's bodies.
                if (t.ContainsGenericParameters && !ReferenceEquals(emittingType, fromClass))
                    return false;
                callTarget = TypeBuilder.GetMethod(t, method);
                return true;
            }
        }
        return false; // no instantiation of the declaring type found
    }

    /// <summary>
    /// Resolves the call target for <c>super.method()</c> / inherited-method dispatch when
    /// the method may be declared on a generic base of <paramref name="currentClass"/>.
    /// Pass-through for methods on non-generic types.
    /// </summary>
    public static bool TryResolveSuperCall(
        TypeBuilder? currentClass, MethodInfo method, Type? emittingType, out MethodInfo callTarget)
    {
        callTarget = method;
        if (method.DeclaringType is not TypeBuilder declType || !declType.IsGenericTypeDefinition)
            return true;
        if (currentClass == null)
            return false;
        return TryResolveOnBaseChain(currentClass, declType, method, emittingType, out callTarget);
    }

    private static bool ContainsTypeBuilder(Type t)
    {
        if (t is TypeBuilder) return true;
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
        {
            // TypeBuilder.MakeGenericType / TypeBuilder.MakeGenericMethod produce
            // TypeBuilderInstantiation — a Type whose generic definition is a TypeBuilder
            // even when all type arguments are plain RuntimeType. Routing through plain
            // Type.GetConstructor on those throws NotSupportedException.
            if (t.GetGenericTypeDefinition() is TypeBuilder) return true;
            foreach (var arg in t.GetGenericArguments())
                if (ContainsTypeBuilder(arg)) return true;
        }
        if (t.HasElementType && t.GetElementType() is { } elem)
            return ContainsTypeBuilder(elem);
        return false;
    }
}
