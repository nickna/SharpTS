using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Guest class evaluations, independent of their pre-emitted CLR templates.</summary>
public sealed class EmittedClassDefinitionRuntime
{
    internal EmittedClassDefinitionRuntime() { }
    internal sealed record Declarations(TypeBuilder Type, Type InstanceInterface,
        MethodInfo GetDefinition, ConstructorBuilder Constructor, FieldBuilder Template,
        FieldBuilder Factory, FieldBuilder Prototype, MethodBuilder Create, MethodBuilder Construct,
        MethodBuilder ReadProperty, MethodBuilder Invoke);

    private Declarations? _declarations;
    private Declarations Required => _declarations
        ?? throw new InvalidOperationException("Class-definition metadata has not been declared.");
    public bool IsComplete { get; private set; }
    public TypeBuilder Type => Required.Type;
    public Type InstanceInterface => Required.InstanceInterface;
    public MethodInfo GetDefinition => Required.GetDefinition;
    public ConstructorBuilder Constructor => Required.Constructor;
    public FieldBuilder Template => Required.Template;
    public FieldBuilder Factory => Required.Factory;
    public FieldBuilder Prototype => Required.Prototype;
    public MethodBuilder Create => Required.Create;
    public MethodBuilder Construct => Required.Construct;
    public MethodBuilder ReadProperty => Required.ReadProperty;
    public MethodBuilder Invoke => Required.Invoke;

    internal void Declare(Declarations declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        if (_declarations is not null || IsComplete)
            throw new InvalidOperationException("Class-definition metadata has already been declared.");
        if (new MemberInfo[] { declarations.Constructor, declarations.Template, declarations.Factory,
                declarations.Prototype, declarations.Create, declarations.Construct,
                declarations.ReadProperty, declarations.Invoke }
            .Any(member => member.DeclaringType != declarations.Type)
            || declarations.GetDefinition.DeclaringType != declarations.InstanceInterface
            || declarations.InstanceInterface.Module != declarations.Type.Module)
            throw new InvalidOperationException("Class-definition handles must belong to their declared owner.");
        _declarations = declarations;
    }

    internal void CompleteEmission()
    {
        var declarations = Required;
        if (IsComplete) throw new InvalidOperationException("Class-definition emission is already complete.");
        if (new[] { declarations.Create, declarations.Construct, declarations.ReadProperty, declarations.Invoke }
            .Any(method => method.GetILGenerator().ILOffset == 0))
            throw new InvalidOperationException("Class-definition method bodies have not all been emitted.");
        IsComplete = true;
    }
}
