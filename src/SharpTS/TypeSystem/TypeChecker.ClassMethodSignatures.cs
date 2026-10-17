using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record ClassMethodSignature(TypeInfo.Function Body, List<TypeInfo.TypeParameter>? Parameters);
    private readonly Dictionary<Stmt.Function, ClassMethodSignature> _classMethodSignatures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeInfo.Function, List<TypeInfo.TypeParameter>?> _classMethodImplementationParameters = new(ReferenceEqualityComparer.Instance);

    private TypeInfo BuildClassMethodSignature(Stmt.Function method, bool preparatory = false, bool wrapGeneratorReturn = true)
    {
        var signatureEnvironment = new TypeEnvironment(_environment);
        using var scope = new EnvironmentScope(this, signatureEnvironment);
        if (!preparatory) ValidateCircularTypeParameterConstraints(method.TypeParams);
        List<TypeInfo.TypeParameter>? typeParameters = method.TypeParams is { Count: > 0 } declarations
            ? BuildGenericTypeParameters(declarations, signatureEnvironment) : null;
        var (parameters, required, rest, names) = BuildFunctionSignature(method.Parameters,
            validateDefaults: !preparatory, contextName: $"method '{method.Name.Lexeme}'", editorOwner: method,
            inspectDestructuredDefaults: !preparatory);
        for (int i = 0; i < names.Count; i++) signatureEnvironment.Define(names[i], parameters[i]);
        TypeInfo result = ResolveAnnotation(method.ReturnType, method.ReturnTypeNode, method, EditorAnnotationSlot.Return)
            ?? (preparatory ? TypeInfo.Any.Shared : TypeInfo.Inferred.Shared);
        if (wrapGeneratorReturn && method.ReturnType is not null && method.IsGenerator)
            result = method.IsAsync && result is not TypeInfo.AsyncGenerator ? new TypeInfo.AsyncGenerator(result)
                : !method.IsAsync && result is not TypeInfo.Generator ? new TypeInfo.Generator(result) : result;
        var body = new TypeInfo.Function(parameters, result, required, rest,
            ResolveAnnotation(method.ThisType, method.ThisTypeNode, method, EditorAnnotationSlot.This), names);
        _classMethodSignatures[method] = new(body, typeParameters);
        _classMethodImplementationParameters[body] = typeParameters;
        TypeInfo callable = ClassMethodCallable(body, typeParameters);
        _typeMap.SetClassMethodImplementation(method, body, callable);
        RegisterEditorSignature(body, method, method.Name);
        RegisterEditorSignature(callable, method, method.Name);
        return callable;
    }

    private static TypeInfo ClassMethodCallable(TypeInfo.Function body, List<TypeInfo.TypeParameter>? parameters) =>
        parameters is { Count: > 0 }
            ? new TypeInfo.GenericFunction(parameters, body.ParamTypes, body.ReturnType, body.RequiredParams,
                body.HasRestParam, body.ThisType, body.ParamNames) : body;

    private TypeInfo.Function ClassMethodBodySignature(Stmt.Function method, TypeEnvironment environment)
    {
        if (!_classMethodSignatures.TryGetValue(method, out var signature))
        {
            BuildClassMethodSignature(method, wrapGeneratorReturn: method.ComputedKey is null);
            signature = _classMethodSignatures[method];
        }
        if (signature.Parameters is { } parameters && method.TypeParams is { } declarations)
            for (int i = 0; i < parameters.Count; i++) DefineSourceTypeParameter(environment, declarations[i], parameters[i]);
        return signature.Body;
    }

    private TypeInfo BuildClassMethodOverload(List<TypeInfo> signatures, TypeInfo implementation)
    {
        var body = ClassMethodImplementation(implementation);
        TypeInfo callable = signatures.All(signature => signature is TypeInfo.Function)
            ? new TypeInfo.OverloadedFunction(signatures.Cast<TypeInfo.Function>().ToList(), body)
            : new TypeInfo.OverloadSet(signatures);
        _typeMap.SetClassMethodImplementation(callable, body);
        RegisterEditorPublicSignatures(callable, signatures);
        return callable;
    }

    private TypeInfo.Function ClassMethodImplementation(TypeInfo callable) =>
        _typeMap.GetClassMethodImplementation(callable) ?? callable switch
        {
            TypeInfo.Function function => function,
            TypeInfo.GenericFunction generic => new TypeInfo.Function(generic.ParamTypes, generic.ReturnType,
                generic.RequiredParams, generic.HasRestParam, generic.ThisType, generic.ParamNames),
            TypeInfo.OverloadedFunction overloaded => overloaded.Implementation,
            _ => throw new InvalidOperationException("Class method implementation signature is unavailable.")
        };

    private TypeInfo UpdateClassMethodReturn(Stmt.Function method, TypeInfo declared, TypeInfo.Function body, TypeInfo result)
    {
        body = body with { ReturnType = result };
        var signature = _classMethodSignatures[method];
        _classMethodSignatures[method] = signature with { Body = body };
        _classMethodImplementationParameters[body] = signature.Parameters;
        TypeInfo callable = declared switch
        {
            TypeInfo.OverloadedFunction overloaded => overloaded with { Implementation = body },
            TypeInfo.OverloadSet => declared,
            _ => ClassMethodCallable(body, signature.Parameters)
        };
        _typeMap.SetClassMethodImplementation(method, body, callable);
        RegisterEditorSignature(body, method, method.Name);
        RegisterEditorSignature(callable, method, method.Name);
        CopyEditorPublicSignatures(declared, callable);
        return callable;
    }
}
