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
/// <c>delete</c>, spread, freeze) is never observed anywhere those would be needed. A candidate <c>o</c>
/// qualifies iff ALL hold:
/// <list type="number">
///   <item>declared <c>const</c>/<c>let</c> with an initializer that is a <em>simple</em> object literal —
///         every property is a plain <c>key: value</c> (no spread, no computed/string-literal key, no
///         method, no getter/setter, no <c>{ a = 5 }</c> cover-grammar shorthand-default), keys are
///         unique, and every value's static type is a primitive <c>number</c>/<c>boolean</c>/<c>string</c>
///         (which inherently excludes <c>any</c>/<c>undefined</c>-admitting fields a typed slot would
///         silently coerce);</item>
///   <item>the ONLY uses are constant-key field reads <c>o.KEY</c> and writes <c>o.KEY = v</c> where
///         <c>KEY</c> is one of the literal's own fields (and a write's value is the same primitive kind).
///         Any other appearance of the bare variable — argument pass, return, store to another binding,
///         spread, <c>===</c>, <c>typeof</c>, <c>o[expr]</c>, <c>o.unknownKey</c>, <c>delete</c>,
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
    public static void Analyze(List<Stmt> program, TypeMap? typeMap, ClosureAnalyzer? closures)
    {
        if (typeMap == null) return;

        var visitor = new Visitor(typeMap);
        foreach (var stmt in program)
            visitor.Visit(stmt);

        foreach (var (name, candidate) in visitor.Candidates)
        {
            if (visitor.Disqualified.Contains(name)) continue;
            if (visitor.DeclCount.GetValueOrDefault(name) != 1) continue;
            if (closures?.IsVariableCaptured(name) == true) continue;
            typeMap.MarkPromotableObjectLocal(candidate.NameToken, candidate.Shape);
        }
    }

    private sealed class Visitor(TypeMap typeMap) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;

        /// <summary>name → its candidate declaration (name token, shape, and the field-name set for O(1) membership).</summary>
        public Dictionary<string, (Token NameToken, ObjectShapeInfo Shape, HashSet<string> FieldNames)> Candidates { get; } = new();

        /// <summary>How many times each name is declared anywhere (any kind of binding).</summary>
        public Dictionary<string, int> DeclCount { get; } = new();

        /// <summary>Names with at least one disqualifying occurrence.</summary>
        public HashSet<string> Disqualified { get; } = new();

        protected override void VisitVar(Stmt.Var stmt) =>
            HandleDeclaration(stmt.Name, stmt.Initializer);

        protected override void VisitConst(Stmt.Const stmt) =>
            HandleDeclaration(stmt.Name, stmt.Initializer);

        private void HandleDeclaration(Token name, Expr? initializer)
        {
            var lexeme = name.Lexeme;
            DeclCount[lexeme] = DeclCount.GetValueOrDefault(lexeme) + 1;

            if (initializer is Expr.ObjectLiteral lit && !Candidates.ContainsKey(lexeme)
                && TryBuildShape(lit, out var shape, out var fieldNames))
                Candidates[lexeme] = (name, shape, fieldNames);

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

        protected override void VisitVariable(Expr.Variable expr)
        {
            // Catch-all: any bare variable occurrence not consumed by a permitted read/write override
            // is an escape (returned, passed, spread, compared, dynamically indexed, compound-assigned,
            // reassigned, ...).
            Disqualified.Add(expr.Name.Lexeme);
        }

        /// <summary>
        /// Builds the shape for a candidate object literal, or returns false if the literal is not a
        /// simple fixed-shape primitive record. See the class summary for the rules.
        /// </summary>
        private bool TryBuildShape(Expr.ObjectLiteral lit, out ObjectShapeInfo shape, out HashSet<string> fieldNames)
        {
            shape = null!;
            fieldNames = null!;
            if (lit.Properties.Count == 0) return false;

            var fields = new List<ObjectShapeField>(lit.Properties.Count);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in lit.Properties)
            {
                if (prop.IsSpread || prop.Kind != Expr.ObjectPropertyKind.Value || prop.IsShorthandDefault)
                    return false;
                if (prop.Key is not Expr.IdentifierKey idk)
                    return false; // computed / string-literal / numeric keys: not o.KEY-accessible
                var fname = idk.Name.Lexeme;
                if (!names.Add(fname))
                    return false; // duplicate key
                if (ClassifyKind(_typeMap.Get(prop.Value)) is not { } kind)
                    return false; // non-primitive / undefined-admitting field
                fields.Add(new ObjectShapeField(fname, kind));
            }

            var key = string.Join(";", fields.Select(f => f.Name + ":" + f.Kind));
            shape = new ObjectShapeInfo(key, fields);
            fieldNames = names;
            return true;
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
