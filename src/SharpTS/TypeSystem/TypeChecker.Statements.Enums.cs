using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Enum declaration type checking - handles enum statements including const enums with computed values.
/// </summary>
public partial class TypeChecker
{
    private sealed record NumericEnumConstant(double Value);
    private readonly ConditionalWeakTable<BindingSymbol, NumericEnumConstant> _numericEnumConstants = new();
    private readonly ConditionalWeakTable<FrozenDictionary<string, object>, Dictionary<string, object>> _knownEnumMembers = new();

    private void RecordNumericEnumConstant(Stmt.Const declaration, BindingSymbol binding)
    {
        if (declaration.TypeAnnotation is not null || declaration.TypeAnnotationNode is not null ||
            declaration.Initializer is not (Expr.Literal { Value: double } or Expr.Variable or
                Expr.Grouping or Expr.Unary or Expr.Binary))
            return;

        if (ConstEnumExpressionEvaluator.EvaluateKnownMember(declaration.Initializer,
                FrozenDictionary<string, object>.Empty, "", isConst: false, ResolveNumericEnumConstant) is double value)
            _numericEnumConstants.AddOrUpdate(binding, new(value));
    }

    private object? ResolveNumericEnumConstant(Expr expression) => expression is Expr.Variable variable &&
        _environment.GetValueBinding(variable.Name.Lexeme) is { } binding &&
        _numericEnumConstants.TryGetValue(binding, out var constant) ? constant.Value : null;

    private void CheckEnumDeclaration(Stmt.Enum enumStmt)
    {
        var declarationBinding = RegisterValueDeclaration(enumStmt.Name, mergeWithLocal: true);

        Dictionary<string, object> members = [];
        Dictionary<string, object> knownMembers = [];
        double? currentNumericValue = null;
        bool hasNumeric = false;
        bool hasString = false;
        bool autoIncrementActive = true;

        foreach (var member in enumStmt.Members)
        {
            bool isKnown = true;
            if (member.Value != null)
            {
                // For literals, do normal type checking
                // For const enum computed expressions, skip CheckExpr (enum not yet defined)
                Expr initializer = member.Value;
                if (initializer is not Expr.Literal && ConstEnumExpressionEvaluator.TryEvaluateSignedLiteral(initializer, out double signedValue))
                    initializer = new Expr.Literal(signedValue);
                if (initializer is Expr.Literal lit)
                {
                    if (lit.Value is double d)
                    {
                        // Numeric literal - enable auto-increment from this value
                        members[member.Name.Lexeme] = d;
                        currentNumericValue = d + 1;
                        hasNumeric = true;
                        autoIncrementActive = true;
                    }
                    else if (lit.Value is string s)
                    {
                        // String literal - disable auto-increment
                        members[member.Name.Lexeme] = s;
                        hasString = true;
                        autoIncrementActive = false;
                    }
                    else
                    {
                        throw new TypeCheckException($" Enum member '{member.Name.Lexeme}' must be a string or number literal.", tsCode: "TS2553");
                    }
                }
                else if (enumStmt.IsConst)
                {
                    // Const enums support computed values (e.g., B = A * 2)
                    var computedValue = EvaluateConstEnumExpression(
                        member.Value, members, enumStmt.Name.Lexeme, ResolveConstInitializerReference);
                    _typeMap.SetConstantEnumInitializer(member.Value, computedValue);
                    if (computedValue is double d)
                    {
                        members[member.Name.Lexeme] = d;
                        currentNumericValue = d + 1;
                        hasNumeric = true;
                        autoIncrementActive = true;
                    }
                    else if (computedValue is string s)
                    {
                        members[member.Name.Lexeme] = s;
                        hasString = true;
                        autoIncrementActive = false;
                    }
                    else
                    {
                        throw new TypeCheckException($" Const enum member '{member.Name.Lexeme}' must evaluate to a string or number.", tsCode: "TS2553");
                    }
                }
                else
                {
                    // Non-const enums may use arbitrary computed numeric expressions, but
                    // only a known constant can start an implicit numeric sequence.
                    // An enum initializer is still visited for its resulting type, but failures in
                    // a subexpression (for example an arbitrary string index that resolves to
                    // `any`) are not themselves enum-declaration diagnostics. Treat those as any
                    // and let definite non-numeric results produce the enum-specific TS18033.
                    TypeInfo computedType;
                    _suppressDiagnostics++;
                    try
                    {
                        var enumInitializerEnvironment = new TypeEnvironment(_environment);
                        foreach (var (resolvedName, resolvedValue) in members)
                        {
                            enumInitializerEnvironment.Define(resolvedName, resolvedValue switch
                            {
                                string => TypeInfo.String.Shared,
                                _ => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
                            });
                        }
                        using (new EnvironmentScope(this, enumInitializerEnvironment))
                            computedType = CheckExpr(member.Value);
                    }
                    catch (TypeCheckException)
                    {
                        computedType = TypeInfo.Any.Shared;
                    }
                    finally
                    {
                        _suppressDiagnostics--;
                    }
                    var knownValue = ConstEnumExpressionEvaluator.EvaluateKnownMember(
                        member.Value, knownMembers, enumStmt.Name.Lexeme, isConst: false, ResolveInitializerReference);
                    if (knownValue is not double && !IsNumber(computedType) && computedType is not
                        TypeInfo.Enum { IsScalar: true, Kind: EnumKind.Numeric })
                    {
                        throw new TypeCheckException(
                            $" Enum member '{member.Name.Lexeme}' must have a numeric computed value.",
                            member.Name.Line,
                            tsCode: "TS18033");
                    }

                    double value = knownValue is double number ? number : double.NaN;
                    members[member.Name.Lexeme] = value;
                    isKnown = knownValue is double;
                    autoIncrementActive = isKnown;
                    currentNumericValue = autoIncrementActive ? value + 1 : null;
                    hasNumeric = true;
                }
            }
            else if (enumStmt.IsAmbient && !enumStmt.IsConst)
            {
                members[member.Name.Lexeme] = double.NaN;
                hasNumeric = true;
                isKnown = false;
            }
            else
            {
                // No initializer - use auto-increment if active
                if (!autoIncrementActive)
                {
                    throw new TypeCheckException(
                        $" Enum member '{member.Name.Lexeme}' must have an initializer.", tsCode: "TS1061");
                }

                currentNumericValue ??= 0;
                members[member.Name.Lexeme] = currentNumericValue.Value;
                hasNumeric = true;
                currentNumericValue++;
            }

            if (isKnown)
                knownMembers[member.Name.Lexeme] = members[member.Name.Lexeme];
        }

        // Determine enum kind
        EnumKind kind = ClassifyEnumMembers(hasNumeric, hasString);

        var enumType = new TypeInfo.Enum(enumStmt.Name.Lexeme, members, kind, enumStmt.IsConst)
        {
            DeclarationBinding = declarationBinding
        };
        if (knownMembers.Count > 0)
            _knownEnumMembers.Add(enumType.Members, knownMembers);
        if (enumStmt.IsConst)
            _typeMap.SetDeclaredValueType(declarationBinding, enumType);
        DeclareValue(enumStmt.Name, enumType, mergeWithLocal: true);
        _environment.DefineType(enumStmt.Name.Lexeme, enumType);

        object? ResolveConstInitializerReference(Expr expression) =>
            expression is Expr.Variable variable && ResolveNumericEnumConstant(variable) is double number &&
            !enumStmt.Members.Any(member => member.Name.Lexeme == variable.Name.Lexeme) ? number : null;

        object? ResolveInitializerReference(Expr expression)
        {
            if (expression is Expr.Variable variable && !members.ContainsKey(variable.Name.Lexeme))
                return ResolveNumericEnumConstant(variable);
            if (_typeMap.Get(expression) is TypeInfo.Enum { MemberName: { } name, Kind: EnumKind.Numeric } source &&
                IsEnumDeclarationMemberReference(expression, source) &&
                _knownEnumMembers.TryGetValue(source.Members, out var values) &&
                values.GetValueOrDefault(name) is double number)
                return number;
            return null;
        }
    }

    private bool IsEnumDeclarationMemberReference(Expr expression, TypeInfo.Enum source)
    {
        var receiver = expression switch
        {
            Expr.Get get => get.Object,
            Expr.GetIndex { Index: Expr.Literal { Value: string } } index => index.Object,
            _ => null,
        };
        BindingSymbol? binding = receiver switch
        {
            Expr.Variable variable => _environment.GetValueBinding(variable.Name.Lexeme),
            Expr.Get member when _typeMap.Get(member.Object) is TypeInfo.Namespace ns =>
                ns.GetValueBinding(member.Name.Lexeme),
            _ => null,
        };
        return binding is not null && ReferenceEquals(binding, source.DeclarationBinding);
    }

    private static EnumKind ClassifyEnumMembers(bool hasNumeric, bool hasString) => (hasNumeric, hasString) switch
    {
        (false, true) => EnumKind.String,
        (true, true) => EnumKind.Heterogeneous,
        _ => EnumKind.Numeric
    };

    /// <summary>
    /// Evaluates a constant expression for const enum members via the shared
    /// <see cref="ConstEnumExpressionEvaluator"/>, mapping failures onto TypeCheckExceptions
    /// with the TS code each error kind has always carried here.
    /// </summary>
    private static object EvaluateConstEnumExpression(
        Expr expr, Dictionary<string, object> resolvedMembers, string enumName, Func<Expr, object?> resolveReference)
    {
        return ConstEnumExpressionEvaluator.Evaluate(expr, resolvedMembers, enumName, static e =>
            new TypeCheckException($" {e.Message}", tsCode: e.Kind switch
            {
                ConstEnumErrorKind.ForwardReference => "TS2474",
                ConstEnumErrorKind.InvalidOperandTypes => "TS2362",
                _ => "TS2553",
            }), resolveReference: resolveReference);
    }
}
