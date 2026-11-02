using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Preserves raw call results in return slots before any CLR signatures are defined.
/// Callee summaries use AST identities, so forward declarations and declaration order
/// cannot change the result. Missing binding proofs and recursive cycles stay boxed.
/// </summary>
internal sealed partial class ForwardedReturnValueAnalyzer(TypeMap map, RuntimeFeatureSet? features,
    RawCallStorageAnalysis? storage = null)
{
    private readonly Dictionary<Expr.Call, Stmt.Function> _targets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Function, bool> _rawReturns = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Function, TokenType?> _returnKinds = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Stmt.Function> _visiting = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Stmt.Function> _nativeVisiting = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Stmt.Function> _numericReturnVisiting = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr> _rawExpressions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, Stmt.Class> _classes = [];

    internal static void Analyze(List<Stmt> statements, TypeMap? map, RuntimeFeatureSet? features,
        IReadOnlyList<Modules.ParsedModule>? modules, RawCallStorageAnalysis? storage = null)
    {
        if (map is null) return;
        var analysis = new ForwardedReturnValueAnalyzer(map, features, storage);
        if (storage is not null)
            foreach (var module in storage.Modules) analysis.ResolveBindings(module);
        else if (modules is null) analysis.ResolveBindings(statements);
        else foreach (var module in modules)
            if (!module.IsCommonJs) analysis.ResolveBindings(module.Statements);

        var inventory = new Inventory(map, analysis._classes);
        foreach (var statement in storage?.Statements ?? statements) inventory.Visit(statement);
        foreach (var expression in (IEnumerable<Expr>?)storage?.Returns ?? inventory.Returns)
            if (ReturnSlotAnalysis.ExpressionReturnMayBeUndefined(expression, map))
                analysis._rawExpressions.Add(expression);
        // Compute every decision before publishing flags: a summary must not depend
        // on which caller happened to be visited first.
        bool changed;
        do
        {
            // A newly raw local can change a callee's result and then its callers' storage.
            // Cached clean summaries from an earlier storage epoch are not authoritative.
            analysis._rawReturns.Clear(); analysis._returnKinds.Clear();
            analysis._visiting.Clear(); analysis._nativeVisiting.Clear();
            int expressions = analysis._rawExpressions.Count;
            changed = storage?.Propagate(analysis.PassesRawCall) == true;
            if (storage?.HasMutableDateStaticProducer == true || storage?.HasErasedGenericFieldProducer == true
                || storage?.HasDynamicNumericArgumentProducer == true || storage?.HasTypedArrayValueReadProducer == true)
                foreach (var call in storage.Calls)
                    foreach (var expression in storage.EquivalentExpressions(call))
                        _ = analysis.PassesRawCall(expression, RawCallStorageAnalysis.Evidence.Numeric);
            foreach (var expression in (IEnumerable<Expr>?)storage?.Returns ?? inventory.Returns)
                _ = analysis.PassesRawCall(expression);
            changed |= expressions != analysis._rawExpressions.Count;
        } while (changed);
        if (storage is not null)
            storage.Publish(analysis._rawExpressions, analysis.FunctionReturnsRaw, statements);
        else
            foreach (var expression in analysis._rawExpressions) map.MarkUndefinedReachableReturn(expression);
    }

    private void ResolveBindings(IReadOnlyList<Stmt> statements)
    {
        var stable = new HashSet<Stmt.Function>(ReferenceEqualityComparer.Instance);
        StableFunctionBindingAnalyzer.Analyze(statements, stable);
        var hazards = new BindingHazards();
        foreach (var statement in statements) hazards.Visit(statement);
        if (hazards.DynamicBindings) return;
        stable.RemoveWhere(function => hazards.ImportedNames.Contains(function.Name.Lexeme));
        foreach (var (function, calls) in NumericRestCallBindingAnalyzer.Analyze(statements, stable.ToList(), stable))
            foreach (var call in calls) _targets[call] = function;
        ResolveIntrinsicCalls(statements);
    }

    private bool PassesRawCall(Expr expression) => PassesRawCall(expression, RawCallStorageAnalysis.Evidence.Any);

    private bool PassesRawCall(Expr expression, RawCallStorageAnalysis.Evidence evidence)
    {
        bool raw = expression switch
        {
            Expr.Call call => (evidence != RawCallStorageAnalysis.Evidence.Numeric || storage?.HasNumericProducer == true)
                && (IsPrimitive(map.Get(call)) && !CallReturnsNative(call)
                    || map.Get(call) is TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Inferred
                    || map.Get(call) is TypeInfo.Primitive { Type: TokenType.TYPE_STRING } or TypeInfo.String or TypeInfo.StringLiteral
                        && _targets.TryGetValue(call, out var stringTarget)
                        && FunctionReturnsRawValue(stringTarget, RawCallStorageAnalysis.Evidence.Any, _stringReturnVisiting)
                    || evidence == RawCallStorageAnalysis.Evidence.Numeric
                        && map.Get(call) is TypeInfo.BigInt or TypeInfo.BigIntLiteral
                        && _targets.TryGetValue(call, out var target) && FunctionReturnsRawNumeric(target)
                    || call.Arguments.Any(argument => PassesRawCall(argument, evidence))),
            Expr.Variable variable => storage?.IsRawBinding(variable.Name, evidence) == true,
            Expr.Grouping group => PassesRawCall(group.Expression, evidence),
            Expr.TypeAssertion assertion => PassesRawCall(assertion.Expression, evidence),
            Expr.NonNullAssertion assertion => PassesRawCall(assertion.Expression, evidence),
            Expr.Satisfies satisfies => PassesRawCall(satisfies.Expression, evidence),
            // Visit both sides: nested conditional merges also need their own hint.
            Expr.Ternary conditional => PassesRawCall(conditional.ThenBranch, evidence) | PassesRawCall(conditional.ElseBranch, evidence),
            Expr.Logical logical => PassesRawCall(logical.Left, evidence) | PassesRawCall(logical.Right, evidence),
            Expr.NullishCoalescing nullish => PassesRawCall(nullish.Left, evidence) | PassesRawCall(nullish.Right, evidence),
            Expr.Comma comma => PassesRawCall(comma.Right, evidence),
            Expr.Assign assignment => PassesRawCall(assignment.Value, evidence),
            Expr.AssertedAssignment assignment => evidence == RawCallStorageAnalysis.Evidence.Any | PassesRawCall(assignment.LoweredAssignment, evidence),
            Expr.Set assignment => PassesRawCall(assignment.Value, evidence),
            Expr.SetSuper assignment => PassesRawCall(assignment.Value, evidence),
            Expr.SetIndex assignment => PassesRawCall(assignment.Value, evidence),
            Expr.SetPrivate assignment => PassesRawCall(assignment.Value, evidence),
            Expr.DestructuringAssign assignment => PassesRawCall(assignment.ResultValue, evidence),
            Expr.LogicalAssign assignment => storage?.IsRawBinding(assignment.Name, evidence) == true
                | PassesRawCall(assignment.Value, evidence),
            Expr.LogicalSet assignment => PassesRawCall(assignment.Value, evidence),
            Expr.LogicalSetPrivate assignment => PassesRawCall(assignment.Value, evidence),
            Expr.LogicalSetIndex assignment => PassesRawCall(assignment.Value, evidence),
            Expr.Binary binary when NumericResultMayRetainBigInt(binary.Operator.Type) => evidence != RawCallStorageAnalysis.Evidence.Call
                && (PassesRawCall(binary.Left, RawCallStorageAnalysis.Evidence.Numeric)
                    | PassesRawCall(binary.Right, RawCallStorageAnalysis.Evidence.Numeric)),
            Expr.Unary unary when unary.Operator.Type is TokenType.MINUS or TokenType.TILDE => evidence != RawCallStorageAnalysis.Evidence.Call
                && (map.IsRawNumericResult(unary) || PassesRawCall(unary.Right, RawCallStorageAnalysis.Evidence.Numeric)),
            Expr.TaggedTemplateLiteral tagged => (evidence != RawCallStorageAnalysis.Evidence.Numeric || storage?.HasNumericProducer == true)
                && (IsPrimitive(map.Get(tagged)) || map.Get(tagged) is TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Inferred
                    || tagged.Expressions.Any(argument => PassesRawCall(argument, evidence))),
            Expr.ArrayLiteral array => array.Elements.Any(value => PassesRawCall(value is Expr.Spread spread ? spread.Expression : value, evidence)),
            Expr.ObjectLiteral literal => literal.Properties.Any(property => PassesRawCall(property.Value, evidence)),
            Expr.New creation => creation.Arguments.Any(argument => PassesRawCall(argument, evidence)),
            Expr.Get member => (evidence != RawCallStorageAnalysis.Evidence.Call
                    && PrimitiveFieldValueAnalysis.IsErasedGenericField(member, map))
                | PassesRawCall(member.Object, evidence),
            Expr.GetIndex member => (evidence != RawCallStorageAnalysis.Evidence.Call
                    && PrimitiveFieldValueAnalysis.IsErasedGenericField(member, map))
                | PassesRawCall(member.Object, evidence),
            Expr.Spread spread => PassesRawCall(spread.Expression, evidence),
            // Other operators perform their own conversions; their operands are not returned.
            _ => false
        };
        // Existing checker representation facts survive cloning independently of whether
        // their cause was a call. They must not become raw-CALL optimizer veto evidence.
        raw |= evidence == RawCallStorageAnalysis.Evidence.Any && map.IsUndefinedReachableReturn(expression)
            || evidence == RawCallStorageAnalysis.Evidence.Numeric && map.IsRawNumericResult(expression);
        if (raw)
        {
            _rawExpressions.Add(expression);
            if (evidence == RawCallStorageAnalysis.Evidence.Numeric) _ = map.MarkRawNumericResult(expression);
        }
        return raw;
    }

    private static bool NumericResultMayRetainBigInt(TokenType operation) => operation is
        TokenType.PLUS or TokenType.MINUS or TokenType.STAR or TokenType.SLASH or TokenType.PERCENT or TokenType.STAR_STAR or
        TokenType.AMPERSAND or TokenType.PIPE or TokenType.CARET or TokenType.LESS_LESS or TokenType.GREATER_GREATER or
        TokenType.GREATER_GREATER_GREATER;

    private readonly HashSet<Stmt.Function> _stringReturnVisiting = new(ReferenceEqualityComparer.Instance);

    private bool FunctionReturnsRawNumeric(Stmt.Function function) =>
        FunctionReturnsRawValue(function, RawCallStorageAnalysis.Evidence.Numeric, _numericReturnVisiting);

    private bool FunctionReturnsRawValue(Stmt.Function function, RawCallStorageAnalysis.Evidence evidence,
        HashSet<Stmt.Function> visiting)
    {
        // This is negative evidence from an actual stable target body, not a
        // native-return classification. Cycles alone establish no producer;
        // the surrounding fixed point revisits callers after real evidence grows.
        if (!visiting.Add(function)) return false;
        try
        {
            var returns = new Returns();
            foreach (var statement in function.Body ?? []) returns.Visit(statement);
            return returns.Values.Any(value => PassesRawCall(value, evidence));
        }
        finally { visiting.Remove(function); }
    }

    private bool FunctionReturnsRaw(Stmt.Function function)
    {
        if (_rawReturns.TryGetValue(function, out bool raw)) return raw;
        if (!_visiting.Add(function)) return true;
        var returns = new Returns();
        foreach (var statement in function.Body ?? []) returns.Visit(statement);
        var kind = returns.Values.Count == 0 ? null : PrimitiveKind(map.Get(returns.Values[0]));
        _returnKinds[function] = kind;
        raw = function.IsAsync || function.IsGenerator || function.TypeParams is { Count: > 0 } ||
            function.Body is null || returns.Values.Count == 0 ||
            returns.Values.Any(value => kind is null || PrimitiveKind(map.Get(value)) != kind ||
                ReturnSlotAnalysis.ExpressionReturnMayBeUndefined(value, map) || PassesRawCall(value));
        _visiting.Remove(function);
        _rawReturns[function] = raw;
        return raw;
    }

    private bool CallReturnsNative(Expr.Call call)
    {
        if (call.Optional || !IsPrimitive(map.Get(call))) return false;
        if (_nativeIntrinsics.TryGetValue(call, out var kind)) return kind == PrimitiveKind(map.Get(call));
        if (_targets.TryGetValue(call, out var function)) return !FunctionReturnsRaw(function) &&
            _returnKinds[function] == PrimitiveKind(map.Get(call));
        if (call.Callee is not Expr.Get { Optional: false } method || !EquivalentMethods(method).Any(map.IsStableExactPrimitiveMethodCall) ||
            map.Get(method.Object) is not TypeInfo.Instance instance) return false;
        var core = Core(instance.ResolvedClassType);
        if (!TryFindMethod(core, method.Name.Lexeme, out var owner, out var target)) return false;
        var expectedKind = PrimitiveKind(map.Get(call));
        return EquivalentMethods(method).Any(map.IsNativePrimitiveFieldMethodCall) &&
                PrimitiveKind(map.GetClassMethodImplementation(target!)?.ReturnType) == expectedKind ||
            MethodReturnsNative(owner!, target!, expectedKind);
    }

    private IEnumerable<Expr.Get> EquivalentMethods(Expr.Get method) =>
        storage is null ? [method] : storage.EquivalentExpressions(method).OfType<Expr.Get>();

    private bool TryFindMethod(ClassMetadataCore? core, string name, out Stmt.Class? owner, out Stmt.Function? method)
    {
        owner = null;
        method = null;
        var seen = new HashSet<int>();
        while (core is not null && seen.Add(core.DeclarationId))
        {
            if (!_classes.TryGetValue(core.DeclarationId, out var source) ||
                source.IsDeclare || source.SuperclassExpr is not (null or Expr.Variable or Expr.Get) ||
                source.Decorators is { Count: > 0 } || source.TypeParams is { Count: > 0 } ||
                source.Methods.Any(candidate => candidate.Name.Lexeme == "constructor" && HasReturn(candidate)) ||
                source.Fields.Any(field => !field.IsStatic && (field.ComputedKey is not null || field.Name.Lexeme == name)) ||
                source.Accessors?.Any(accessor => !accessor.IsStatic && (accessor.ComputedKey is not null || accessor.Name.Lexeme == name)) == true ||
                source.AutoAccessors?.Any(accessor => !accessor.IsStatic && accessor.Name.Lexeme == name) == true ||
                source.Methods.Any(candidate => !candidate.IsStatic && candidate.ComputedKey is not null)) return false;
            var candidates = source.Methods.Where(candidate => !candidate.IsStatic && candidate.Name.Lexeme == name).ToArray();
            if (method is null && candidates.Length != 0)
            {
                if (candidates.Length != 1) return false;
                owner = source;
                method = candidates[0];
                if (method.IsPrivate || method.IsAbstract || method.IsAsync || method.IsGenerator ||
                    method.Decorators is { Count: > 0 } || method.TypeParams is { Count: > 0 }) return false;
            }
            // Even an overriding method's receiver can be replaced by a constructor
            // higher in the hierarchy. Validate every ancestor before trusting it.
            var parent = Core(core.Superclass);
            if (parent is null && (core.Superclass is not null || source.SuperclassExpr is not null)) return false;
            core = parent;
        }
        return core is null && method is not null;
    }

    private bool MethodReturnsNative(Stmt.Class owner, Stmt.Function method, TokenType? expectedKind)
    {
        // This proof is deliberately narrower than typed-core eligibility. An exact
        // receiver alone does not prove that the public object ABI returns a primitive
        // (parameters and fields can contain null/undefined despite their annotations).
        if (!_nativeVisiting.Add(method)) return false;
        bool native = method.Body is [Stmt.Return { Value: { } value }] &&
            expectedKind is not null && PrimitiveKind(map.Get(value)) == expectedKind &&
            !ReturnSlotAnalysis.ExpressionReturnMayBeUndefined(value, map) && Native(value, owner);
        _nativeVisiting.Remove(method);
        return native;
    }

    private bool Native(Expr expression, Stmt.Class owner) => expression switch
    {
        Expr.Literal { Value: double or bool } => true,
        Expr.Grouping group => NativeWrapper(group, group.Expression, owner),
        Expr.TypeAssertion assertion => NativeWrapper(assertion, assertion.Expression, owner),
        Expr.NonNullAssertion assertion => NativeWrapper(assertion, assertion.Expression, owner),
        Expr.Satisfies satisfies => NativeWrapper(satisfies, satisfies.Expression, owner),
        Expr.Unary { Operator.Type: TokenType.PLUS or TokenType.BANG } => true,
        Expr.Binary binary => IsPrimitive(map.Get(binary)) && Native(binary.Left, owner) && Native(binary.Right, owner),
        Expr.Call { Callee: Expr.Super { Method: { } name }, Optional: false } superCall =>
            features?.UsesDynamicPropertyDescriptors == false && !features.UsesClassPrototypeMutation &&
            !features.UsesObjectIntegrityMutation &&
            TryFindMethod(Core(map.GetClassType(owner)?.Superclass), name.Lexeme, out var parent, out var method) &&
            MethodReturnsNative(parent!, method!, PrimitiveKind(map.Get(superCall))),
        Expr.Call call => CallReturnsNative(call),
        _ => false
    };

    private bool NativeWrapper(Expr wrapper, Expr value, Stmt.Class owner) =>
        PrimitiveKind(map.Get(wrapper)) is { } kind && kind == PrimitiveKind(map.Get(value)) && Native(value, owner);

    private static bool HasReturn(Stmt.Function method)
    {
        var returns = new Returns();
        foreach (var statement in method.Body ?? []) returns.Visit(statement);
        return returns.Values.Count != 0;
    }

    private static ClassMetadataCore? Core(TypeInfo? type) => type is TypeInfo.MutableClass mutable
        ? mutable.FreezeCore() : ClassInfoAccessor.Get(type, value => value.Core, value => value.Core);

    private static bool IsPrimitive(TypeInfo? type) => type is TypeInfo.Primitive
        { Type: TokenType.TYPE_NUMBER or TokenType.TYPE_BOOLEAN } or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral;

    private static TokenType? PrimitiveKind(TypeInfo? type) => type switch
    {
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral => TokenType.TYPE_NUMBER,
        TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } or TypeInfo.BooleanLiteral => TokenType.TYPE_BOOLEAN,
        _ => null
    };

    private sealed class Returns : AstVisitorBase
    {
        internal List<Expr> Values { get; } = [];
        protected override void VisitReturn(Stmt.Return statement) { if (statement.Value is { } value) Values.Add(value); }
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
    }

    private sealed class Inventory(TypeMap map, Dictionary<int, Stmt.Class> classes) : AstVisitorBase
    {
        internal List<Expr> Returns { get; } = [];
        protected override void VisitReturn(Stmt.Return statement)
        {
            if (statement.Value is { } value) Returns.Add(value);
            base.VisitReturn(statement);
        }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            if (expression.ExpressionBody is { } body) Returns.Add(body);
            base.VisitArrowFunction(expression);
        }
        protected override void VisitClass(Stmt.Class statement)
        {
            if (map.GetClassType(statement) is { } type) classes[type.Core.DeclarationId] = statement;
            base.VisitClass(statement);
        }
    }

    private sealed class BindingHazards : AstVisitorBase
    {
        internal bool DynamicBindings { get; private set; }
        internal HashSet<string> ImportedNames { get; } = new(StringComparer.Ordinal);
        protected override void VisitVariable(Expr.Variable expression)
        {
            if (expression.Name.Lexeme is "eval" or "globalThis" or "Function") DynamicBindings = true;
        }
        protected override void VisitThis(Expr.This expression) => DynamicBindings = true;
        protected override void VisitImport(Stmt.Import statement)
        {
            if (statement.DefaultImport is { } name) ImportedNames.Add(name.Lexeme);
            if (statement.NamespaceImport is { } ns) ImportedNames.Add(ns.Lexeme);
            foreach (var import in statement.NamedImports ?? []) ImportedNames.Add((import.LocalName ?? import.Imported).Lexeme);
        }
        protected override void VisitImportAlias(Stmt.ImportAlias statement) => DynamicBindings = true;
        protected override void VisitImportRequire(Stmt.ImportRequire statement) => DynamicBindings = true;
    }
}
