using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Private data-reader transport for actual emitted ES module namespace producers.</summary>
public sealed class EmittedModuleNamespaceRuntime
{
    internal EmittedModuleNamespaceRuntime(Type readerType, ConstructorInfo readerConstructor, MethodBuilder create, MethodBuilder read, MethodBuilder tryRead)
    {
        if (!ReferenceEquals(create.Module, read.Module) || !ReferenceEquals(create.DeclaringType, read.DeclaringType) ||
            !ReferenceEquals(create.Module, tryRead.Module) || !ReferenceEquals(create.DeclaringType, tryRead.DeclaringType))
            throw new InvalidOperationException("Module namespace operations must have the same actual output owner.");
        ReaderType = readerType;
        ReaderConstructor = readerConstructor;
        Create = create;
        Read = read;
        TryRead = tryRead;
    }

    public Type ReaderType { get; }
    public ConstructorInfo ReaderConstructor { get; }
    public MethodBuilder Create { get; }
    public MethodBuilder Read { get; }
    public MethodBuilder TryRead { get; }
}
