using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Guest class evaluations, independent of their pre-emitted CLR templates.</summary>
public sealed class EmittedClassDefinitionRuntime
{
    internal EmittedClassDefinitionRuntime() { }
    internal sealed record Declarations(TypeBuilder Type, Type InstanceInterface,
        MethodInfo GetDefinition, ConstructorBuilder Constructor, FieldBuilder Template,
        FieldBuilder Factory, FieldBuilder Prototype, FieldBuilder Keys, FieldBuilder Parent, FieldBuilder Captures, FieldBuilder PrivateMembers, MethodBuilder Create, MethodBuilder Construct,
        MethodBuilder ReadProperty, MethodBuilder Invoke, TypeBuilder CaptureType, ConstructorBuilder CaptureConstructor,
        FieldBuilder CaptureOwner, FieldBuilder CaptureField, MethodBuilder ReadCapture, MethodBuilder WriteCapture,
        MethodBuilder FindEnvironment, MethodBuilder ValidateParent, MethodBuilder InitializeReceiver,
        MethodBuilder GetParent, MethodBuilder ReadArgument, MethodBuilder ReadPrototypeProperty, MethodBuilder ReadSuper);

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
    public FieldBuilder Keys => Required.Keys;
    public FieldBuilder Parent => Required.Parent;
    public FieldBuilder Captures => Required.Captures;
    public FieldBuilder PrivateMembers => Required.PrivateMembers;
    public MethodBuilder Create => Required.Create;
    public MethodBuilder Construct => Required.Construct;
    public MethodBuilder ReadProperty => Required.ReadProperty;
    public MethodBuilder Invoke => Required.Invoke;
    public ConstructorBuilder CaptureConstructor => Required.CaptureConstructor;
    public MethodBuilder ReadCapture => Required.ReadCapture;
    public MethodBuilder WriteCapture => Required.WriteCapture;
    public MethodBuilder FindEnvironment => Required.FindEnvironment;
    public MethodBuilder ValidateParent => Required.ValidateParent;
    public MethodBuilder InitializeReceiver => Required.InitializeReceiver;
    public MethodBuilder GetParent => Required.GetParent;
    public MethodBuilder ReadArgument => Required.ReadArgument;
    public MethodBuilder ReadPrototypeProperty => Required.ReadPrototypeProperty;
    public MethodBuilder ReadSuper => Required.ReadSuper;

    internal void Declare(Declarations declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        if (_declarations is not null || IsComplete)
            throw new InvalidOperationException("Class-definition metadata has already been declared.");
        if (new MemberInfo[] { declarations.Constructor, declarations.Template, declarations.Factory,
                declarations.Prototype, declarations.Keys, declarations.Parent, declarations.Captures, declarations.PrivateMembers, declarations.Create, declarations.Construct,
                declarations.ReadProperty, declarations.Invoke, declarations.ReadCapture, declarations.WriteCapture,
                declarations.FindEnvironment, declarations.ValidateParent, declarations.InitializeReceiver,
                declarations.GetParent, declarations.ReadArgument, declarations.ReadPrototypeProperty, declarations.ReadSuper }
            .Any(member => member.DeclaringType != declarations.Type)
            || declarations.GetDefinition.DeclaringType != declarations.InstanceInterface
            // A completed interface exposes RuntimeModule, while its in-memory
            // owner still exposes RuntimeModuleBuilder. Their MVID is the same
            // module identity even though the reflection wrappers differ.
            || declarations.InstanceInterface.Module.ModuleVersionId != declarations.Type.Module.ModuleVersionId
            || declarations.CaptureType.Module != declarations.Type.Module
            || new MemberInfo[] { declarations.CaptureConstructor, declarations.CaptureOwner, declarations.CaptureField }
                .Any(member => member.DeclaringType != declarations.CaptureType))
            throw new InvalidOperationException("Class-definition handles must belong to their declared owner.");
        _declarations = declarations;
    }

    internal void CompleteEmission()
    {
        var declarations = Required;
        if (IsComplete) throw new InvalidOperationException("Class-definition emission is already complete.");
        if (new[] { declarations.Create, declarations.Construct, declarations.ReadProperty, declarations.Invoke,
                declarations.ReadCapture, declarations.WriteCapture, declarations.FindEnvironment,
                declarations.ValidateParent, declarations.InitializeReceiver, declarations.GetParent,
                declarations.ReadArgument, declarations.ReadPrototypeProperty, declarations.ReadSuper }
            .Any(method => method.GetILGenerator().ILOffset == 0))
            throw new InvalidOperationException("Class-definition method bodies have not all been emitted.");
        IsComplete = true;
    }
}
