using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Required class-definition initialization and exception-unwrapping metadata.</summary>
public sealed class EmittedClassInitializationRuntime
{
    internal EmittedClassInitializationRuntime() { }
    public bool IsComplete { get; private set; }
    private MethodBuilder? _runDefinition;
    private ConstructorBuilder? _canonicalStaticStorageAttributeConstructor;
    private bool _bodyEmitted;
    internal NativeSourceOwnerCatalog? SourceOwners { get; private set; }
    internal System.Reflection.MethodInfo? NativeInitializer { get; private set; }
    internal System.Reflection.MethodInfo? NativeStaticMemberPresence { get; private set; }

    internal void DeclareNativeStaticMemberPresence(System.Reflection.MethodInfo lookup)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(lookup);
        if (NativeStaticMemberPresence != null) throw new InvalidOperationException("Native static-member presence is already declared.");
        NativeStaticMemberPresence = lookup;
    }

    internal void DeclareNativeInitializer(System.Reflection.MethodInfo initializer)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(initializer);
        if (NativeInitializer != null) throw new InvalidOperationException("Native initializer is already declared.");
        NativeInitializer = initializer;
    }

    internal void DeclareSourceOwners(NativeSourceOwnerCatalog catalog)
    {
        EnsureMutable();
        if (SourceOwners != null) throw new InvalidOperationException("Source-owner catalog is already declared.");
        SourceOwners = catalog;
    }

    /// <summary>Forces a CLR class initializer and rethrows its original guest exception.</summary>
    public MethodBuilder RunDefinition
    {
        get => _runDefinition ?? throw new InvalidOperationException("Class-initialization helper has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            if (_runDefinition is not null)
                throw new InvalidOperationException("Class-initialization helper has already been declared.");
            _runDefinition = value;
        }
    }

    /// <summary>Marks source classes whose deferred static body uses canonical generic storage.</summary>
    public ConstructorBuilder CanonicalStaticStorageAttributeConstructor
    {
        get => _canonicalStaticStorageAttributeConstructor ?? throw new InvalidOperationException("Canonical static-storage attribute has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            if (_canonicalStaticStorageAttributeConstructor is not null)
                throw new InvalidOperationException("Canonical static-storage attribute has already been declared.");
            _canonicalStaticStorageAttributeConstructor = value;
        }
    }

    internal void MarkBodyEmitted()
    {
        EnsureMutable();
        _ = RunDefinition;
        if (_bodyEmitted)
            throw new InvalidOperationException("Class-initialization helper body has already been emitted.");
        _bodyEmitted = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Class-initialization metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = RunDefinition;
        _ = CanonicalStaticStorageAttributeConstructor;
        if (!_bodyEmitted)
            throw new InvalidOperationException("Class-initialization helper body has not been emitted.");
        IsComplete = true;
    }
}
