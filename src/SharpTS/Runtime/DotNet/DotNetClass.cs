using System.Reflection;
using SharpTS.Execution;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Runtime representation of a <c>@DotNetType</c>-annotated TypeScript class.
/// Bound into the runtime environment at class-declaration time. Implements
/// <see cref="ISharpTSCallable"/> so <c>new X(...)</c> dispatches through the
/// existing callable path; also exposes static members via <see cref="GetStaticMember"/>.
/// </summary>
public sealed class DotNetClass : ISharpTSCallable, ITypeCategorized
{
    public string TypeScriptName { get; }
    public Type Type { get; }

    /// <summary>Per-method overload hints sourced from <c>@DotNetOverload</c> decorators.</summary>
    internal IReadOnlyDictionary<string, string> OverloadHints { get; }

    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.External;

    // Lazily created for static event subscriptions.
    private Dictionary<(string, ISharpTSCallable), Delegate>? _eventSubscriptions;

    public DotNetClass(string typeScriptName, Type type, IReadOnlyDictionary<string, string>? overloadHints = null)
    {
        TypeScriptName = typeScriptName;
        Type = type;
        OverloadHints = overloadHints ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public int Arity()
    {
        var ctors = ManagedDotNetInterop.GetConstructors(
            Type, BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Length == 0) return 0;
        int min = int.MaxValue;
        foreach (var c in ctors)
        {
            int required = c.GetParameters().Count(p => !p.HasDefaultValue);
            if (required < min) min = required;
        }
        return min == int.MaxValue ? 0 : min;
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Value type default construction: treat `new Struct()` with zero args as default(T).
        if (Type.IsValueType && arguments.Count == 0)
        {
            return new DotNetInstance(ManagedDotNetInterop.CreateInstance(Type)!, Type);
        }

        var ctors = ManagedDotNetInterop.GetConstructors(
            Type, BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Length == 0)
        {
            throw new Runtime.Exceptions.ThrowException(DotNetExceptionMapper.Map(
                new MissingMethodException($"No public constructors found on external type '{Type.FullName}'.")));
        }

        var candidate = DotNetMethodResolver.ResolveConstructor(
            ctors, arguments,
            OverloadHints.TryGetValue("constructor", out var hint) ? hint : null);
        var ctor = (ConstructorInfo)candidate.Method;

        var invokeArgs = DotNetMethod.BuildInvokeArgs(ctor.GetParameters(), arguments, candidate, interpreter);
        return DotNetInstance.InvokeWithMapping(() =>
        {
            var obj = ctor.Invoke(invokeArgs);
            return new DotNetInstance(obj, Type);
        });
    }

    /// <summary>
    /// Evaluates <c>ClassName.member</c> for static access.
    /// </summary>
    public object? GetStaticMember(string name)
    {
        // Static event subscription API.
        if (name == "addEventListener")
        {
            return new DotNetEventBinder(Type, receiver: null, GetOrCreateSubscriptions(), isAdd: true);
        }
        if (name == "removeEventListener")
        {
            return new DotNetEventBinder(Type, receiver: null, GetOrCreateSubscriptions(), isAdd: false);
        }

        OverloadHints.TryGetValue(name, out var hint);

        var methods = DotNetTypeRegistry.GetMethods(Type, name, isStatic: true);
        if (methods.Length > 0)
        {
            return new DotNetMethod(methods, receiver: null, name, hint);
        }

        var member = DotNetTypeRegistry.GetPropertyOrField(Type, name, isStatic: true);
        return member switch
        {
            PropertyInfo pi => DotNetMarshaller.WrapReturn(
                DotNetInstance.InvokeWithMapping(() => pi.GetValue(null)), pi.PropertyType),
            FieldInfo fi => DotNetMarshaller.WrapReturn(fi.GetValue(null), fi.FieldType),
            _ => SharpTSUndefined.Instance
        };
    }

    public void SetStaticMember(string name, object? value, Interpreter? interpreter = null)
    {
        var member = DotNetTypeRegistry.GetPropertyOrField(Type, name, isStatic: true);
        switch (member)
        {
            case PropertyInfo pi when pi.CanWrite:
                DotNetInstance.InvokeWithMapping(() => pi.SetValue(null, DotNetMarshaller.Convert(value, pi.PropertyType, interpreter)));
                return;
            case FieldInfo fi when !fi.IsInitOnly:
                fi.SetValue(null, DotNetMarshaller.Convert(value, fi.FieldType, interpreter));
                return;
            default:
                throw new Runtime.Exceptions.ThrowException(DotNetExceptionMapper.Map(
                    new MissingMemberException(
                        $"Static property or field '{name}' not found (or is read-only) on '{Type.FullName}'.")));
        }
    }

    private Dictionary<(string, ISharpTSCallable), Delegate> GetOrCreateSubscriptions()
    {
        return _eventSubscriptions ??= new Dictionary<(string, ISharpTSCallable), Delegate>();
    }

    public override string ToString() => $"[DotNetType {TypeScriptName} → {Type.FullName}]";
}
