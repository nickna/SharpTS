using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private void BindEditorClassSelf(object owner, TypeEnvironment environment, TypeInfo type)
    {
        if (!ShouldCaptureEditorFacts) return;
        Token? name = owner switch { Stmt.Class declaration => declaration.Name, Expr.ClassExpr expression => expression.Name, _ => null };
        if (name is null || GetEditorDeclaration(name) is not { } source || FindEditorScope(environment) is not { } scope) return;
        EditorFacts.BindLocal(scope, source.Name.Lexeme, BindingNamespace.Value, environment.GetValueBinding(name.Lexeme), type, owner);
        EditorFacts.BindLocal(scope, source.Name.Lexeme, BindingNamespace.Type, environment.GetTypeSymbol(name.Lexeme), type, owner);
    }

    // Use only the completed metadata of this exact source owner. No inherited lookup or
    // annotation resolution is performed here, and every spelling has a written parser view.
    private void CaptureEditorClassDeclarations(object owner, TypeInfo? classType)
    {
        if (!ShouldCaptureEditorFacts || CurrentSourceDocument is not { EditorSyntax: { } syntax } document) return;
        ClassMetadataCore? core = classType switch
        {
            TypeInfo.Class type => type.Core,
            TypeInfo.GenericClass type => type.Core,
            _ => null,
        };
        IReadOnlyList<Stmt.Field> fields;
        IReadOnlyList<Stmt.Function> methods;
        IReadOnlyList<Stmt.Accessor> accessors;
        IReadOnlyList<Stmt.AutoAccessor> autoAccessors;
        switch (owner)
        {
            case Stmt.Class declaration:
                fields = declaration.Fields; methods = declaration.Methods;
                accessors = declaration.Accessors ?? []; autoAccessors = declaration.AutoAccessors ?? []; break;
            case Expr.ClassExpr expression:
                fields = expression.Fields; methods = expression.Methods;
                accessors = expression.Accessors ?? []; autoAccessors = expression.AutoAccessors ?? []; break;
            default: return;
        }
        void Record(object declaration, Token name, TypeInfo? type)
        {
            if (!syntax.GetRecords(declaration).Any(record => record.IsAuthoritative && record.Kind == EditorSyntaxKind.Name &&
                record.Role is EditorSyntaxRole.DeclarationName or EditorSyntaxRole.MemberName or EditorSyntaxRole.PrivateName)) return;
            if (GetWrittenMemberName(document, declaration, name) is not { } written) return;
            EditorFacts.RecordDeclaration(document, declaration, written, null, BindingNamespace.Value, type,
                type is null ? EditorFactAvailability.Unavailable : EditorFactAvailability.Available);
        }
        foreach (var field in fields)
        {
            if (field.ComputedKey is not null || field.IsLiteralName) continue;
            // A lowered parameter property already has a lexical parameter declaration fact.
            if (methods.Any(method => method.Parameters.Any(parameter => parameter.IsParameterProperty && ReferenceEquals(parameter.Name, field.Name)))) continue;
            var types = field.IsPrivate ? field.IsStatic ? core?.StaticPrivateFieldTypes : core?.PrivateFieldTypes :
                field.IsStatic ? core?.StaticProperties : core?.FieldTypes;
            Record(field, field.Name, types?.GetValueOrDefault(field.Name.Lexeme));
        }
        foreach (var method in methods)
        {
            if (method.ComputedKey is not null) continue;
            var types = method.IsPrivate ? method.IsStatic ? core?.StaticPrivateMethodTypes : core?.PrivateMethodTypes :
                method.IsStatic ? core?.StaticMethods : core?.Methods;
            TypeInfo? type = types?.GetValueOrDefault(method.Name.Lexeme);
            if (type is not null && GetEditorPublicSignatures(type) is { } publicSignatures)
                type = publicSignatures.FirstOrDefault(signature => ReferenceEquals(GetEditorSignatureSource(signature)?.Owner, method)) ?? type;
            if (type is TypeInfo.OverloadedFunction overload)
                type = overload.Signatures.FirstOrDefault(signature => ReferenceEquals(GetEditorSignatureSource(signature)?.Owner, method)) ?? type;
            Record(method, method.Name, type);
        }
        foreach (var accessor in accessors)
        {
            if (accessor.ComputedKey is not null) continue;
            // The existing core accessor maps do not separate static/instance collisions.
            bool collision = accessors.Any(other => other.IsStatic != accessor.IsStatic && other.Name.Lexeme == accessor.Name.Lexeme);
            Record(accessor, accessor.Name, collision ? null : accessor.Kind.Type == TokenType.GET
                ? core?.Getters.GetValueOrDefault(accessor.Name.Lexeme) : core?.Setters.GetValueOrDefault(accessor.Name.Lexeme));
        }
        foreach (var accessor in autoAccessors)
            Record(accessor, accessor.Name, accessor.IsStatic ? core?.StaticProperties.GetValueOrDefault(accessor.Name.Lexeme) :
                core?.Getters.GetValueOrDefault(accessor.Name.Lexeme) ?? core?.FieldTypes.GetValueOrDefault(accessor.Name.Lexeme));
    }
}
