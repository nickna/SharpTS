using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Whole-program analysis that flags <c>const</c>/<c>let</c> object-literal locals which can be promoted
/// from the default <c>Dictionary&lt;string, object&gt;</c> to a generated value-type "shape" struct with
/// typed fields (#862). Direct sibling of <see cref="ArrayLocalPromotionAnalyzer"/> and
/// <see cref="NonEscapingArrowLocalAnalyzer"/>: a name qualifies only if it is provably non-escaping, so
/// the promoted struct (which has no dynamic-object semantics — no descriptors, enumerability, prototype,
/// <c>delete</c>, freeze) is never observed anywhere those would be needed. Stable object spread is
/// represented by direct copies between compatible shape structs. An escaping result can materialize
/// fields from a still-promoted source. A candidate <c>o</c>
/// qualifies iff ALL hold:
/// <list type="number">
///   <item>declared <c>const</c>/<c>let</c> with an initializer that is a <em>simple</em> object literal —
///         every property is either a plain <c>key: value</c> or a spread of an earlier candidate local
///         (no computed/string-literal key, method, getter/setter, or <c>{ a = 5 }</c>
///         cover-grammar shorthand-default), and every final value's static type is a primitive
///         <c>number</c>/<c>boolean</c>/<c>string</c>
///         (which inherently excludes <c>any</c>/<c>undefined</c>-admitting fields a typed slot would
///         silently coerce);</item>
///   <item>the ONLY uses are constant-key field reads <c>o.KEY</c>, same-kind writes
///         <c>o.KEY = v</c>, stable spread, proven numeric-only consumers, and direct
///         <c>Object.keys(o)</c> calls. The latter can
///         materialize the immutable key metadata without exposing the struct itself.
///         Any other appearance of the bare variable — argument pass, return, store to another binding,
///         unstable spread, <c>===</c>, <c>typeof</c>, <c>o[expr]</c>, <c>o.unknownKey</c>, <c>delete</c>,
///         compound/logical member assign, reassignment — disqualifies it;</item>
///   <item>the name is declared exactly once in the whole program (conservative guard against scope
///         ambiguity / shadowing without full scope resolution);</item>
///   <item>the name is not captured by any closure (a captured local is routed to an <c>object</c>
///         display-class field, never a typed struct slot the get/set fast path can key on).</item>
/// </list>
///
/// <para>The catch-all is <see cref="Visitor.VisitVariable"/>: any bare variable occurrence not consumed
/// by the permitted-read/write overrides disqualifies the name. The permitted overrides deliberately do
/// NOT recurse into the receiver variable, so only the safe <c>o.KEY</c> shapes survive. Compound and
/// logical member assignment (<c>o.x += v</c>, <c>o.x ??= v</c>) are intentionally NOT permitted in this
/// first cut — they fall through to the catch-all and disqualify (follow-up).</para>
/// </summary>
public static class ObjectLocalPromotionAnalyzer
{
    public static void Analyze(List<Stmt> program, TypeMap? typeMap, ClosureAnalyzer? closures,
        IReadOnlyDictionary<Expr.Call, ObjectConsumerInfo>? consumers = null)
    {
        if (typeMap == null) return;

        var visitor = new Visitor(typeMap, consumers ?? StableObjectConsumerAnalyzer.Analyze(program));
        foreach (var stmt in program)
            visitor.Visit(stmt);

        var eligible = new HashSet<string>(visitor.Candidates.Keys, StringComparer.Ordinal);
        if (visitor.Disqualified.Contains("eval")) eligible.Clear();
        if (visitor.DeclCount.ContainsKey("Object") || visitor.Disqualified.Contains("Object"))
            eligible.ExceptWith(visitor.ObjectKeysReceivers);
        foreach (var name in visitor.Candidates.Keys)
        {
            if (visitor.Disqualified.Contains(name)
                || visitor.DeclCount.GetValueOrDefault(name) != 1
                || closures?.IsVariableCaptured(name) == true)
                eligible.Remove(name);
        }

        // A promoted result requires promoted sources, but a generic result can copy fields
        // from a promoted source directly into its dictionary. Do not propagate an escape
        // backwards into otherwise independent source objects.
        bool changed;
        do
        {
            changed = false;
            foreach (var (source, target) in visitor.SpreadEdges)
            {
                if (!eligible.Contains(source))
                    changed |= eligible.Remove(target);
            }
        } while (changed);

        foreach (var name in eligible)
        {
            var candidate = visitor.Candidates[name];
            typeMap.MarkPromotableObjectLocal(candidate.NameToken, candidate.Shape);
        }
        foreach (var (call, receiver) in visitor.ConsumerCalls)
            if (eligible.Contains(receiver))
                typeMap.MarkPromotedObjectCall(call, visitor.Consumers[call]);
    }

    private sealed class Visitor(TypeMap typeMap, IReadOnlyDictionary<Expr.Call, ObjectConsumerInfo> consumers) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        public IReadOnlyDictionary<Expr.Call, ObjectConsumerInfo> Consumers { get; } = consumers;
        public Dictionary<Expr.Call, string> ConsumerCalls { get; } = new(ReferenceEqualityComparer.Instance);

        public sealed record Candidate(
            Token NameToken,
            ObjectShapeInfo Shape,
            HashSet<string> FieldNames);

        /// <summary>name → its candidate declaration and final merged shape.</summary>
        public Dictionary<string, Candidate> Candidates { get; } = new(StringComparer.Ordinal);

        /// <summary>Stable spread dependency edges (source, result).</summary>
        public List<(string Source, string Target)> SpreadEdges { get; } = [];

        /// <summary>Spread-bearing candidate literal → its declaration, for specialized visitation.</summary>
        private Dictionary<Expr.ObjectLiteral, string> SpreadLiteralOwners { get; } =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>How many times each name is declared anywhere (any kind of binding).</summary>
        public Dictionary<string, int> DeclCount { get; } = new();

        /// <summary>Names with at least one disqualifying occurrence.</summary>
        public HashSet<string> Disqualified { get; } = new();
        public HashSet<string> ObjectKeysReceivers { get; } = new();

        protected override void VisitVar(Stmt.Var stmt) =>
            HandleDeclaration(stmt.Name, stmt.Initializer);

        protected override void VisitConst(Stmt.Const stmt) =>
            HandleDeclaration(stmt.Name, stmt.Initializer);

        private void HandleDeclaration(Token name, Expr? initializer)
        {
            var lexeme = name.Lexeme;
            DeclCount[lexeme] = DeclCount.GetValueOrDefault(lexeme) + 1;

            if (initializer is Expr.ObjectLiteral lit && !Candidates.ContainsKey(lexeme)
                && TryBuildShape(lit, out var shape, out var fieldNames, out var spreadSources))
            {
                Candidates[lexeme] = new Candidate(name, shape, fieldNames);
                if (spreadSources.Count != 0)
                {
                    SpreadLiteralOwners[lit] = lexeme;
                    foreach (var source in spreadSources)
                        SpreadEdges.Add((source, lexeme));
                }
            }

            // Visit the initializer so its sub-uses are accounted for. The literal's own property
            // values reference OTHER variables (e.g. `i` in `{ x: i }`), not `o`, so this never
            // disqualifies the candidate itself.
            if (initializer != null)
                Visit(initializer);
        }

        protected override void VisitGet(Expr.Get expr)
        {
            // `o.KEY` read — permitted when receiver is a candidate variable and KEY is one of its
            // fields. Do NOT recurse into the receiver variable (which would disqualify via the
            // catch-all). A non-optional dot read only.
            if (!expr.Optional && expr.Object is Expr.Variable v
                && Candidates.TryGetValue(v.Name.Lexeme, out var c)
                && c.FieldNames.Contains(expr.Name.Lexeme))
                return;
            base.VisitGet(expr);
        }

        protected override void VisitSet(Expr.Set expr)
        {
            // `o.KEY = v` write — permitted; visit the value but not the receiver variable.
            if (expr.Object is Expr.Variable v
                && Candidates.TryGetValue(v.Name.Lexeme, out var c)
                && c.FieldNames.Contains(expr.Name.Lexeme))
            {
                Visit(expr.Value);
                // The written value must be the SAME primitive kind as the field; otherwise the typed
                // slot would coerce it (a number field written with `any`/string diverges). Disqualify.
                if (ClassifyKind(_typeMap.Get(expr.Value)) != FieldKind(c.Shape, expr.Name.Lexeme))
                    Disqualified.Add(v.Name.Lexeme);
                return;
            }
            base.VisitSet(expr);
        }

        protected override void VisitCall(Expr.Call expr)
        {
            if (Consumers.TryGetValue(expr, out var summary)
                && expr.Arguments is [Expr.Variable argument]
                && Candidates.TryGetValue(argument.Name.Lexeme, out var candidate)
                && _typeMap.Get(expr) is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
                && summary.NumericFields.All(name => candidate.Shape.Fields.Any(
                    field => field.Name == name && field.Kind == TokenType.TYPE_NUMBER)))
            {
                ConsumerCalls[expr] = argument.Name.Lexeme;
                return;
            }
            // Object.keys over a closed promoted shape (#1506) observes only a fresh array of the
            // record's fixed enumerable string keys. The emitter does not load/box the struct.
            if (!expr.Optional && expr.Arguments is [Expr.Variable receiver]
                && expr.Callee is Expr.Get
                {
                    Optional: false,
                    Object: Expr.Variable { Name.Lexeme: "Object" },
                    Name.Lexeme: "keys"
                }
                && Candidates.ContainsKey(receiver.Name.Lexeme))
            {
                ObjectKeysReceivers.Add(receiver.Name.Lexeme);
                return;
            }

            base.VisitCall(expr);
        }

        protected override void VisitObjectLiteral(Expr.ObjectLiteral expr)
        {
            if (!SpreadLiteralOwners.ContainsKey(expr))
            {
                base.VisitObjectLiteral(expr);
                return;
            }

            // A spread source occurrence is the one additional permitted use of a stable candidate.
            // Do not visit that bare variable (the dependency edge handles its eligibility); do visit
            // every explicit initializer so reads, writes, and side effects are analyzed normally.
            foreach (var prop in expr.Properties)
            {
                if (prop.IsSpread && prop.Value is Expr.Variable spreadVar
                    && Candidates.ContainsKey(spreadVar.Name.Lexeme))
                    continue;
                Visit(prop.Value);
            }
        }

        protected override void VisitVariable(Expr.Variable expr)
        {
            // Catch-all: any bare variable occurrence not consumed by a permitted read/write override
            // is an escape (returned, passed, spread, compared, dynamically indexed, compound-assigned,
            // reassigned, ...).
            Disqualified.Add(expr.Name.Lexeme);
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            Disqualified.Add(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        // Mutation operands are not ordinary reads. Traverse without the permitted field-read
        // shortcut, including any grouping/assertion wrappers around the target.
        private sealed class MutationVariables(HashSet<string> names) : AstVisitorBase
        {
            protected override void VisitVariable(Expr.Variable expr) => names.Add(expr.Name.Lexeme);
        }

        protected override void VisitDelete(Expr.Delete expr)
        {
            new MutationVariables(Disqualified).Visit(expr.Operand);
            base.VisitDelete(expr);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expr)
        {
            new MutationVariables(Disqualified).Visit(expr.Operand);
            base.VisitPrefixIncrement(expr);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expr)
        {
            new MutationVariables(Disqualified).Visit(expr.Operand);
            base.VisitPostfixIncrement(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Disqualified.Add(expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
        {
            Disqualified.Add(expr.Name.Lexeme);
            base.VisitLogicalAssign(expr);
        }

        protected override void VisitFunction(Stmt.Function statement)
        {
            CountName(statement.Name);
            foreach (var parameter in statement.Parameters) CountParameter(parameter);
            base.VisitFunction(statement);
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            foreach (var parameter in expression.Parameters) CountParameter(parameter);
            base.VisitArrowFunction(expression);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            CountName(statement.Variable);
            base.VisitForOf(statement);
        }

        protected override void VisitForIn(Stmt.ForIn statement)
        {
            CountName(statement.Variable);
            base.VisitForIn(statement);
        }

        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            if (statement.CatchParam != null) CountName(statement.CatchParam);
            base.VisitTryCatch(statement);
        }

        private void CountName(Token name) => DeclCount[name.Lexeme] = DeclCount.GetValueOrDefault(name.Lexeme) + 1;

        private void CountParameter(Stmt.Parameter parameter)
        {
            CountName(parameter.Name);
            foreach (var property in parameter.DestructuredProperties ?? []) CountName(property.Binding);
        }

        protected override void VisitAccessor(Stmt.Accessor statement)
        {
            if (statement.SetterParam != null) CountParameter(statement.SetterParam);
            base.VisitAccessor(statement);
        }

        protected override void VisitImport(Stmt.Import statement)
        {
            if (statement.DefaultImport != null) CountName(statement.DefaultImport);
            if (statement.NamespaceImport != null) CountName(statement.NamespaceImport);
            foreach (var import in statement.NamedImports ?? []) CountName(import.LocalName ?? import.Imported);
        }

        protected override void VisitImportAlias(Stmt.ImportAlias statement) => CountName(statement.AliasName);
        protected override void VisitImportRequire(Stmt.ImportRequire statement) => CountName(statement.AliasName);

        /// <summary>
        /// Builds the shape for a candidate object literal, or returns false if the literal is not a
        /// simple fixed-shape primitive record. See the class summary for the rules.
        /// </summary>
        private bool TryBuildShape(
            Expr.ObjectLiteral lit,
            out ObjectShapeInfo shape,
            out HashSet<string> fieldNames,
            out List<string> spreadSources)
        {
            shape = null!;
            fieldNames = null!;
            spreadSources = [];
            if (lit.Properties.Count == 0) return false;

            var fields = new List<ObjectShapeField>(lit.Properties.Count);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var fieldIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            bool hasSpread = lit.Properties.Any(p => p.IsSpread);
            foreach (var prop in lit.Properties)
            {
                if (prop.IsSpread)
                {
                    if (prop.Value is not Expr.Variable spreadVar
                        || !Candidates.TryGetValue(spreadVar.Name.Lexeme, out var source))
                        return false;

                    spreadSources.Add(spreadVar.Name.Lexeme);
                    foreach (var sourceField in source.Shape.Fields)
                        UpsertField(sourceField.Name, sourceField.Kind);
                    continue;
                }

                if (prop.Kind != Expr.ObjectPropertyKind.Value || prop.IsShorthandDefault)
                    return false;
                if (prop.Key is not Expr.IdentifierKey idk)
                    return false; // computed / string-literal / numeric keys: not o.KEY-accessible
                var fname = idk.Name.Lexeme;
                if (ClassifyKind(_typeMap.Get(prop.Value)) is not { } kind)
                    return false; // non-primitive / undefined-admitting field
                if (!hasSpread && names.Contains(fname))
                    return false; // retain the original conservative rule for ordinary literals
                UpsertField(fname, kind);
            }

            var key = string.Join(";", fields.Select(f => f.Name + ":" + f.Kind));
            shape = new ObjectShapeInfo(key, fields);
            fieldNames = names;
            return true;

            void UpsertField(string name, TokenType kind)
            {
                if (fieldIndexes.TryGetValue(name, out int index))
                {
                    // Object spread overwrites without moving the key in enumeration order.
                    fields[index] = new ObjectShapeField(name, kind);
                    return;
                }

                fieldIndexes[name] = fields.Count;
                names.Add(name);
                fields.Add(new ObjectShapeField(name, kind));
            }
        }

        private static TokenType FieldKind(ObjectShapeInfo shape, string name)
        {
            foreach (var f in shape.Fields)
                if (f.Name == name) return f.Kind;
            return TokenType.TYPE_NUMBER; // unreachable: callers pass a known field name
        }

        /// <summary>
        /// Classifies a static type as a promotable primitive kind, or null. Only a bare primitive
        /// <c>number</c>/<c>boolean</c>/<c>string</c> qualifies — which inherently excludes
        /// <c>any</c>/<c>unknown</c>/<c>undefined</c> and unions (a value the typed slot would coerce).
        /// </summary>
        private static TokenType? ClassifyKind(TypeInfo? type) => type switch
        {
            TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => TokenType.TYPE_NUMBER,
            TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => TokenType.TYPE_BOOLEAN,
            // `string` is TypeInfo.String, never Primitive(TYPE_STRING) (#1108) — match the canonical
            // form so string-valued fields are promotable (the shape struct emits a String slot for them).
            TypeInfo.String => TokenType.TYPE_STRING,
            _ => null
        };
    }
}
