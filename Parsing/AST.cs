using SharpTS.TypeSystem;

namespace SharpTS.Parsing;

/// <summary>
/// Access modifier for class members.
/// </summary>
public enum AccessModifier { Public, Private, Protected }

/// <summary>
/// Type parameter in generic declarations (e.g., T in &lt;T extends Base&gt;, &lt;T = string&gt;, &lt;const T&gt;, or &lt;out T&gt;).
/// </summary>
/// <param name="Name">The type parameter identifier token.</param>
/// <param name="Constraint">Optional constraint type (after extends keyword).</param>
/// <param name="Default">Optional default type (after = sign).</param>
/// <param name="IsConst">Whether this is a const type parameter (TypeScript 5.0+ feature for preserving literal types).</param>
/// <param name="Variance">Variance annotation (in, out, in out) for TypeScript 4.7+ variance modifiers.</param>
/// <param name="ConstraintNode">Node twin of <paramref name="Constraint"/> (type-AST migration), when the parser produced one.</param>
/// <param name="DefaultNode">Node twin of <paramref name="Default"/> (type-AST migration), when the parser produced one.</param>
public record TypeParam(
    Token Name,
    string? Constraint,
    string? Default = null,
    bool IsConst = false,
    TypeParameterVariance Variance = TypeParameterVariance.Invariant,
    TypeNode? ConstraintNode = null,
    TypeNode? DefaultNode = null
);

/// <summary>
/// Base record for all expression AST nodes.
/// </summary>
/// <remarks>
/// Expressions evaluate to values. Nested records define specific expression types:
/// literals, variables, binary/unary operations, function calls, property access,
/// array/object literals, arrow functions, etc. Produced by <see cref="Parser"/>,
/// validated by <see cref="TypeChecker"/>, and evaluated by <see cref="Interpreter"/>
/// or compiled by <see cref="ILCompiler"/>.
/// </remarks>
/// <seealso cref="Stmt"/>
public abstract record Expr
{
    /// <summary>Comma (sequence) expression: evaluates all sub-expressions left-to-right, returns the last value.</summary>
    public record Comma(Expr Left, Expr Right) : Expr;
    /// <summary>
    /// Destructuring assignment to existing l-values — <c>[a, b] = rhs</c> / <c>({a, b} = rhs)</c> (#754).
    /// Unlike declaration destructuring (which binds new variables), the targets are existing
    /// <see cref="Variable"/>/<see cref="Get"/>/<see cref="GetIndex"/> l-values. The parser lowers the
    /// pattern (already parsed as an array/object literal) into <paramref name="Assignments"/> — a temp
    /// declaration for the rhs, the per-target assignment statements (reusing the #685 iterator-protocol
    /// normalization <c>__arrayDestructure</c> and <c>__objectRest</c>), and any nested temps — plus
    /// <paramref name="ResultValue"/>, the temp holding the original rhs (an assignment expression
    /// evaluates to its right-hand side, per ECMA-262). Every backend lowers it identically: run
    /// <c>Assignments</c>, then yield <c>ResultValue</c>. <c>Assignments</c> contains only synthesized
    /// <see cref="Stmt.Var"/> and <see cref="Stmt.Expression"/> statements (no control flow), so it is
    /// safe in any expression position.
    /// <para><paramref name="RawTarget"/>/<paramref name="RawDefault"/> retain the un-lowered pattern and
    /// default RHS so that when this node was eager-parsed as a nested element WITH a default
    /// (<c>[[a] = []]</c>, <c>{p: {x} = {}}</c>) the outer pattern walk can re-lower the inner pattern
    /// against the defaulted access instead of the pre-built (wrong-source) statements (#779). Both are
    /// null for a top-level assignment-destructuring, where only <c>Assignments</c>/<c>ResultValue</c> are
    /// used; backends never read them.</para>
    /// </summary>
    public record DestructuringAssign(
        List<Stmt> Assignments,
        Expr ResultValue,
        Expr? RawTarget = null,
        Expr? RawDefault = null) : Expr;
    public record Binary(Expr Left, Token Operator, Expr Right) : Expr;
    public record Logical(Expr Left, Token Operator, Expr Right) : Expr;
    public record NullishCoalescing(Expr Left, Expr Right) : Expr;
    public record Ternary(Expr Condition, Expr ThenBranch, Expr ElseBranch) : Expr;
    public record Grouping(Expr Expression) : Expr;
    public record Literal(object? Value) : Expr;
    public record Unary(Token Operator, Expr Right) : Expr;
    public record Delete(Token Keyword, Expr Operand) : Expr;
    public record Variable(Token Name) : Expr;
    /// <param name="IsVarRedeclaration">True when this assignment was synthesized by
    /// <see cref="VarHoister"/> from a duplicate <c>var</c> declaration — an incompatible value
    /// then reports TS2403 (subsequent declarations must have the same type), not TS2322.</param>
    /// <param name="RedeclarationTypeAnnotation">Set (with <see cref="RedeclarationTypeAnnotationNode"/>)
    /// when <see cref="VarHoister"/> synthesizes a self-assignment (<c>z = z</c>) for an
    /// annotation-only duplicate <c>var</c> (<c>var z: T;</c> with no initializer). The type checker
    /// compares THIS annotation — not the value's type — against the established declared type using
    /// structural identity for TS2403. The self-assignment is a runtime no-op that preserves the
    /// existing binding's value.</param>
    public record Assign(
        Token Name,
        Expr Value,
        bool IsVarRedeclaration = false,
        string? RedeclarationTypeAnnotation = null,
        TypeNode? RedeclarationTypeAnnotationNode = null) : Expr;
    // TypeArgNodes: per-element node twins of TypeArgs (type-AST migration) — same length as
    // TypeArgs when non-null; an element without node support is null without discarding siblings.
    public record Call(Expr Callee, Token Paren, List<string>? TypeArgs, List<Expr> Arguments, bool Optional = false, List<TypeNode?>? TypeArgNodes = null) : Expr;
    // Defaulted: this property read was synthesized by destructuring desugaring and is covered
    // by a default (its own, or a default on an enclosing pattern). The type checker treats a
    // missing property as `undefined` for such reads instead of reporting TS2339, since the
    // wrapping `=== undefined ? default : read` ternary (or an enclosing default) supplies the
    // value. A non-defaulted read stays strict (`const { a } = {}` is still an error). See #796.
    public record Get(Expr Object, Token Name, bool Optional = false, bool Defaulted = false) : Expr;
    public record Set(Expr Object, Token Name, Expr Value) : Expr;
    /// <summary>Private field access: obj.#field</summary>
    public record GetPrivate(Expr Object, Token Name) : Expr;
    /// <summary>Private field assignment: obj.#field = value</summary>
    public record SetPrivate(Expr Object, Token Name, Expr Value) : Expr;
    /// <summary>Private method call: obj.#method(args)</summary>
    public record CallPrivate(Expr Object, Token Name, List<Expr> Arguments) : Expr;
    public record This(Token Keyword) : Expr;
    /// <summary>
    /// New expression: new Callee(args) or new Callee&lt;T&gt;(args).
    /// Callee can be a Variable (class name), Get (namespace path), or any expression.
    /// TypeArgNodes: per-element node twins of TypeArgs (type-AST migration), same length when non-null.
    /// </summary>
    public record New(Expr Callee, List<string>? TypeArgs, List<Expr> Arguments, List<TypeNode?>? TypeArgNodes = null) : Expr;
    /// <summary>
    /// Array literal. Elided positions ([1, , 3]) carry an undefined literal in
    /// Elements (so the type checker and destructuring see a uniform shape) plus
    /// their index in HoleIndices so evaluation/emission can produce a true
    /// ECMA-262 hole instead of an undefined element.
    /// </summary>
    public record ArrayLiteral(List<Expr> Elements, IReadOnlySet<int>? HoleIndices = null) : Expr
    {
        public bool IsHole(int index) => HoleIndices?.Contains(index) == true;
    }
    public record ObjectLiteral(List<Property> Properties) : Expr
    {
        /// <summary>
        /// Marks whether this is a "fresh" object literal (created directly in assignment context).
        /// Fresh literals are subject to excess property checking in TypeScript strict mode.
        /// </summary>
        public bool IsFresh { get; init; } = false;
    }
    // Property key types for object literals: identifier, string/number literal, or computed [expr]
    public abstract record PropertyKey;
    public record IdentifierKey(Token Name) : PropertyKey;
    public record LiteralKey(Token Literal) : PropertyKey;  // STRING or NUMBER token
    public record ComputedKey(Expr Expression) : PropertyKey;

    /// <summary>
    /// Object property kinds for distinguishing value properties from getters/setters.
    /// </summary>
    public enum ObjectPropertyKind { Value, Getter, Setter, Method }

    /// <summary>
    /// Object literal property definition.
    /// </summary>
    /// <param name="Key">The property key (null for spread)</param>
    /// <param name="Value">The property value/getter body/setter body</param>
    /// <param name="IsSpread">Whether this is a spread property (...obj)</param>
    /// <param name="Kind">The kind of property (value, getter, setter, method)</param>
    /// <param name="SetterParam">The setter parameter (for Kind=Setter only)</param>
    /// <param name="IsShorthandDefault">True for the cover-grammar form <c>{ a = 5 }</c> (an ES
    /// CoverInitializedName). The value is stored as <c>Expr.Assign(a, 5)</c> so it round-trips to the
    /// #754 assignment-destructuring lowering; this flag distinguishes it from the legal expression
    /// <c>{ a: a = 5 }</c> so a pure-expression object literal can be rejected as tsc does (#780).</param>
    public record Property(
        PropertyKey? Key,
        Expr Value,
        bool IsSpread = false,
        ObjectPropertyKind Kind = ObjectPropertyKind.Value,
        Stmt.Parameter? SetterParam = null,
        bool IsShorthandDefault = false);
    public record GetIndex(Expr Object, Expr Index, bool Optional = false) : Expr;
    public record SetIndex(Expr Object, Expr Index, Expr Value) : Expr;
    public record Super(Token Keyword, Token? Method) : Expr;  // Method is null for super() constructor calls
    // Compound assignment
    public record CompoundAssign(Token Name, Token Operator, Expr Value) : Expr;
    public record CompoundSet(Expr Object, Token Name, Token Operator, Expr Value) : Expr;
    public record CompoundSetIndex(Expr Object, Expr Index, Token Operator, Expr Value) : Expr;
    // Logical assignment (&&=, ||=, ??=) - has short-circuit semantics
    public record LogicalAssign(Token Name, Token Operator, Expr Value) : Expr;
    public record LogicalSet(Expr Object, Token Name, Token Operator, Expr Value) : Expr;
    public record LogicalSetIndex(Expr Object, Expr Index, Token Operator, Expr Value) : Expr;
    // Increment/decrement
    public record PrefixIncrement(Token Operator, Expr Operand) : Expr;
    public record PostfixIncrement(Expr Operand, Token Operator) : Expr;
    // Arrow function and function expression
    /// <summary>
    /// Arrow function or named function expression.
    /// Name is the function expression name (null for arrow functions and anonymous function expressions).
    /// Named function expressions have their name visible inside the function body for recursion.
    /// ThisType is for type annotations only (arrow expressions cannot have this parameter).
    /// HasOwnThis indicates this binds its own 'this' (function expressions) vs capturing from enclosing scope (arrows).
    /// IsAsync indicates this is an async function that returns a Promise.
    /// IsGenerator indicates this is a generator function (function*) that can yield values.
    /// ThisTypeNode/ReturnTypeNode are the node twins of ThisType/ReturnType (type-AST
    /// migration), populated when the parser produced them.
    /// </summary>
    public record ArrowFunction(Token? Name, List<TypeParam>? TypeParams, string? ThisType, List<Stmt.Parameter> Parameters, Expr? ExpressionBody, List<Stmt>? BlockBody, string? ReturnType, bool HasOwnThis = false, bool IsAsync = false, bool IsGenerator = false, TypeNode? ThisTypeNode = null, TypeNode? ReturnTypeNode = null) : Expr
    {
        // #945: marks the sync forwarding arrow NestedFunctionLifter substitutes for a capturing nested
        // generator that was hoisted into a generator encloser's body. Tells the generator function-DC
        // pass (ComputeMutatedCapturedGeneratorVars) to route this arrow's read-only forwarded captures
        // through the function display class so the hoisted arrow reads them live, not a stale snapshot.
        public bool IsLiftedForwarder { get; init; }
    }
    // Template literal. InvalidEscapeLines carries the source line of each part whose cooked value
    // had an invalid escape sequence (`\xtraordinary`, `\u{hello}`, ...) — a real syntax error for an
    // untagged template (TS1125), but recoverable: the parser substitutes an empty string for that
    // part rather than aborting the whole file, and the checker reports it as a normal diagnostic.
    public record TemplateLiteral(List<string> Strings, List<Expr> Expressions, List<int>? InvalidEscapeLines = null) : Expr;
    // Tagged template literal: tag`template ${expr}`
    public record TaggedTemplateLiteral(
        Expr Tag,                     // The tag function expression
        List<string?> CookedStrings,  // Processed escapes (null for invalid)
        List<string> RawStrings,      // Literal text (unprocessed)
        List<Expr> Expressions        // Interpolated expressions
    ) : Expr;
    // Spread expression for calls and array literals
    public record Spread(Expr Expression) : Expr;
    // Type assertion: value as Type. TargetTypeNode is the structured form of TargetType (type-AST
    // migration) when the parser could build one; the checker resolves it node-first so a composite
    // target (e.g. a conditional type with a function-type extends clause) bypasses the string
    // resolver's scanning hazards. Null for `as const` and any construct without node support.
    public record TypeAssertion(Expr Expression, string TargetType, TypeNode? TargetTypeNode = null) : Expr;
    // Satisfies operator: value satisfies Type (TS 4.9+) - validates without widening.
    // ConstraintTypeNode mirrors TypeAssertion.TargetTypeNode: the structured form resolved
    // node-first so a composite constraint bypasses the string resolver's scanning hazards.
    public record Satisfies(Expr Expression, string ConstraintType, TypeNode? ConstraintTypeNode = null) : Expr;
    // Await expression: await expr (only valid inside async functions)
    public record Await(Token Keyword, Expr Expression) : Expr;
    // Dynamic import: import(pathExpr) - returns Promise of module namespace
    public record DynamicImport(Token Keyword, Expr PathExpression) : Expr;
    // import.meta expression - provides module metadata (url, etc.)
    public record ImportMeta(Token Keyword) : Expr;
    // Yield expression: yield expr or yield* expr (only valid inside generator functions)
    public record Yield(Token Keyword, Expr? Value, bool IsDelegating) : Expr;
    // Regex literal: /pattern/flags
    public record RegexLiteral(string Pattern, string Flags) : Expr;
    // Non-null assertion: expr! (asserts value is not null/undefined at compile time)
    public record NonNullAssertion(Expr Expression) : Expr;
    /// <summary>
    /// Class expression: class [Name] [extends Base] [implements Interfaces] { members }
    /// Name is optional (anonymous class) but visible inside class body for self-reference when present.
    /// </summary>
    public record ClassExpr(
        Token? Name,
        List<TypeParam>? TypeParams,
        Expr? SuperclassExpr,
        List<string>? SuperclassTypeArgs,
        List<Stmt.Function> Methods,
        List<Stmt.Field> Fields,
        List<Stmt.Accessor>? Accessors = null,
        List<Stmt.AutoAccessor>? AutoAccessors = null,
        List<Token>? Interfaces = null,
        List<List<string>>? InterfaceTypeArgs = null,
        bool IsAbstract = false,
        List<Stmt>? StaticInitializers = null,
        // Node twins of SuperclassTypeArgs / InterfaceTypeArgs (type-AST migration); inner lists
        // stay index-aligned with their string twins.
        List<TypeNode?>? SuperclassTypeArgNodes = null,
        List<List<TypeNode?>>? InterfaceTypeArgNodes = null
    ) : Expr;

    /// <summary>
    /// Extracts the full dotted name from a superclass expression (e.g., "ns.Base" from ns.Base).
    /// </summary>
    public static string? GetSuperclassName(Expr? superclassExpr) => superclassExpr switch
    {
        Variable v => v.Name.Lexeme,
        Get g => GetSuperclassName(g.Object) + "." + g.Name.Lexeme,
        _ => null
    };

    /// <summary>
    /// Extracts just the leaf (final identifier) from a superclass expression (e.g., "Base" from ns.Base).
    /// </summary>
    public static string? GetSuperclassLeafName(Expr? superclassExpr) => superclassExpr switch
    {
        Variable v => v.Name.Lexeme,
        Get g => g.Name.Lexeme,
        _ => null
    };

    /// <summary>
    /// Extracts the Token from a superclass expression for line number reporting.
    /// </summary>
    public static Token? GetSuperclassToken(Expr? superclassExpr) => superclassExpr switch
    {
        Variable v => v.Name,
        Get g => g.Name,
        _ => null
    };
}

/// <summary>
/// Decorator applied to a class, method, accessor, property, or parameter.
/// Expression is the decorator expression (Variable, Get, or Call for factories).
/// </summary>
public record Decorator(Token AtToken, Expr Expression);

/// <summary>
/// Base record for all statement AST nodes.
/// </summary>
/// <remarks>
/// Statements perform actions but don't produce values. Nested records define specific
/// statement types: variable declarations, functions, classes, control flow (if, while,
/// for, switch), try/catch, return, break, continue, etc. Produced by <see cref="Parser"/>,
/// validated by <see cref="TypeChecker"/>, and executed by <see cref="Interpreter"/>
/// or compiled by <see cref="ILCompiler"/>.
/// </remarks>
/// <seealso cref="Expr"/>
public abstract record Stmt
{
    public record Expression(Expr Expr) : Stmt;
    /// <param name="TypeAnnotationNode">Structured form of <c>TypeAnnotation</c> when the
    /// construct has node support (type-AST migration); null otherwise — consumers fall back to
    /// the string.</param>
    /// <param name="HoistTypeInferenceInitializer">Set by <see cref="VarHoister"/> on a synthetic
    /// hoisted <c>var</c> whose first real declaration was a nested, annotation-less initializer
    /// (e.g. <c>if (c) { var z = "hello"; }</c>). The binding has no <c>Initializer</c> here (the
    /// initializer runs at its original, rewritten position) and no annotation, so its declared type
    /// would otherwise default to <c>any</c> — suppressing TS2403 for a later <c>var z: number;</c>.
    /// The type checker infers the binding's declared type from this expression (widened, errors
    /// suppressed since they surface at the original site); the interpreter and IL compiler ignore it.</param>
    /// <param name="InitializerContext">A synthetic contextual type used ONLY to guide inference of the
    /// initializer (not as the binding's declared type — it imposes no TS2322 compatibility check and the
    /// inferred type is still kept). Set by the destructuring desugarer to carry the binding pattern's
    /// shape so a mixed array literal source infers as a tuple instead of an array (#783). Erased at
    /// runtime (interpreter and IL compiler ignore it).</param>
    public record Var(Token Name, string? TypeAnnotation, Expr? Initializer, bool HasDefiniteAssignmentAssertion = false, bool IsVar = false, TypeNode? TypeAnnotationNode = null, Expr? HoistTypeInferenceInitializer = null, TypeNode? InitializerContext = null, bool IsDeclare = false) : Stmt;
    /// <summary>
    /// Const variable declaration. Separate from Var for cleaner const-specific handling (e.g., unique symbol).
    /// Initializer is non-nullable since const always requires initialization.
    /// </summary>
    public record Const(Token Name, string? TypeAnnotation, Expr Initializer, TypeNode? TypeAnnotationNode = null) : Stmt;
    /// <summary>
    /// Function or method declaration. Body is null for overload signatures (declaration only).
    /// ThisType is the explicit this parameter type annotation (e.g., this: MyClass).
    /// IsAsync indicates this is an async function that returns a Promise.
    /// IsGenerator indicates this is a generator function (function*) that can yield values.
    /// Decorators contains any @decorator annotations applied to this function/method.
    /// ComputedKey is non-null for a computed symbol-keyed class method (e.g.
    /// <c>[Symbol.iterator]() {}</c>); Name is then a synthetic <c>&lt;computed&gt;</c> token and the
    /// key expression is evaluated at class-definition time (interpreter) / registered as a
    /// symbol method (compiler).
    /// HasDynamicThis is set ONLY on a synthetic generator declaration lifted from a
    /// <c>HasOwnThis</c> generator function expression / object generator method
    /// (<see cref="SharpTS.Parsing.GeneratorArrowLifter"/>). It means "this stub binds its own
    /// dynamic receiver": the compiler threads a leading <c>__this</c> argument into the generator
    /// state machine's <c>&lt;&gt;4__this</c> field, and the interpreter binds the call receiver
    /// (defaulting to <c>undefined</c>) so <c>this</c> inside the body resolves (#775).
    /// ThisTypeNode/ReturnTypeNode are the node twins of ThisType/ReturnType (type-AST
    /// migration), populated when the parser produced them.
    /// </summary>
    public record Function(Token Name, List<TypeParam>? TypeParams, string? ThisType, List<Parameter> Parameters, List<Stmt>? Body, string? ReturnType, bool IsStatic = false, AccessModifier Access = AccessModifier.Public, bool IsAbstract = false, bool IsOverride = false, bool IsAsync = false, bool IsGenerator = false, List<Decorator>? Decorators = null, bool IsPrivate = false, bool IsDeclare = false, Expr? ComputedKey = null, bool HasDynamicThis = false, TypeNode? ThisTypeNode = null, TypeNode? ReturnTypeNode = null) : Stmt;
    public record Parameter(Token Name, string? Type, Expr? DefaultValue = null, bool IsRest = false, bool IsParameterProperty = false, AccessModifier? Access = null, bool IsReadonly = false, bool IsOptional = false, List<Decorator>? Decorators = null, TypeNode? TypeAnnotationNode = null);
    /// <summary>
    /// Class field declaration. For computed property names (e.g., [Symbol("key")]: type),
    /// ComputedKey contains the expression and Name is a synthetic token.
    /// </summary>
    public record Field(Token Name, string? TypeAnnotation, Expr? Initializer, bool IsStatic = false, AccessModifier Access = AccessModifier.Public, bool IsReadonly = false, bool IsOptional = false, bool HasDefiniteAssignmentAssertion = false, List<Decorator>? Decorators = null, bool IsPrivate = false, bool IsDeclare = false, Expr? ComputedKey = null, TypeNode? TypeAnnotationNode = null) : Stmt;
    /// <summary>
    /// Getter/setter declaration. For computed accessor names (e.g., static get [Symbol.species]()),
    /// ComputedKey contains the expression and Name is a synthetic token.
    /// </summary>
    public record Accessor(Token Name, Token Kind, Parameter? SetterParam, List<Stmt> Body, string? ReturnType, AccessModifier Access = AccessModifier.Public, bool IsAbstract = false, bool IsOverride = false, List<Decorator>? Decorators = null, bool IsStatic = false, Expr? ComputedKey = null, TypeNode? ReturnTypeNode = null) : Stmt;
    /// <summary>
    /// Auto-accessor field declaration (TypeScript 4.9+): accessor name: Type = initializer
    /// Automatically generates a private backing field with implicit getter/setter.
    /// </summary>
    /// <param name="Name">The property name token.</param>
    /// <param name="TypeAnnotation">Optional type annotation.</param>
    /// <param name="Initializer">Optional initializer expression.</param>
    /// <param name="IsStatic">Whether this is a static auto-accessor.</param>
    /// <param name="Access">Access modifier (public, private, protected).</param>
    /// <param name="IsReadonly">Whether this is readonly (no setter).</param>
    /// <param name="IsOverride">Whether this overrides a parent accessor.</param>
    /// <param name="Decorators">Optional list of decorators applied to this accessor.</param>
    /// <param name="TypeAnnotationNode">Node twin of <paramref name="TypeAnnotation"/> (type-AST migration), when the parser produced one.</param>
    public record AutoAccessor(
        Token Name,
        string? TypeAnnotation,
        Expr? Initializer,
        bool IsStatic = false,
        AccessModifier Access = AccessModifier.Public,
        bool IsReadonly = false,
        bool IsOverride = false,
        List<Decorator>? Decorators = null,
        TypeNode? TypeAnnotationNode = null
    ) : Stmt;
    /// <summary>
    /// Class declaration. IsDeclare indicates an ambient declaration (declare class) which has no implementation.
    /// StaticInitializers contains static fields and static blocks in declaration order for proper initialization sequencing.
    /// </summary>
    // SuperclassTypeArgNodes / InterfaceTypeArgNodes: node twins of the heritage type-argument
    // string lists (type-AST migration); index-aligned with their string twins when non-null.
    public record Class(Token Name, List<TypeParam>? TypeParams, Expr? SuperclassExpr, List<string>? SuperclassTypeArgs, List<Stmt.Function> Methods, List<Stmt.Field> Fields, List<Stmt.Accessor>? Accessors = null, List<Stmt.AutoAccessor>? AutoAccessors = null, List<Token>? Interfaces = null, List<List<string>>? InterfaceTypeArgs = null, bool IsAbstract = false, List<Decorator>? Decorators = null, bool IsDeclare = false, List<Stmt>? StaticInitializers = null, List<Stmt.IndexSignature>? IndexSignatures = null, List<TypeNode?>? SuperclassTypeArgNodes = null, List<List<TypeNode?>>? InterfaceTypeArgNodes = null) : Stmt;
    /// <summary>
    /// Static block: static { statements }
    /// Executes once when the class is initialized, in declaration order with static fields.
    /// </summary>
    public record StaticBlock(List<Stmt> Body) : Stmt;
    /// <summary>
    /// Interface declaration with optional call and constructor signatures.
    /// </summary>
    public record Interface(
        Token Name,
        List<TypeParam>? TypeParams,
        List<InterfaceMember> Members,
        List<IndexSignature>? IndexSignatures = null,
        List<string>? Extends = null,
        List<CallSignature>? CallSignatures = null,
        List<ConstructorSignature>? ConstructorSignatures = null,
        // Whole-entry node twins of Extends (type-AST migration): each entry is a full type
        // reference ("Base", "Base<T>"), index-aligned with Extends when non-null.
        List<TypeNode?>? ExtendsNodes = null
    ) : Stmt;
    /// <param name="IsMethod">Declared with method syntax (<c>m(x): T</c>) rather than as a
    /// function-typed property — method members keep bivariant parameter relating under
    /// strictFunctionTypes.</param>
    public record InterfaceMember(Token Name, string Type, bool IsOptional = false, bool IsReadonly = false, bool IsMethod = false, TypeNode? TypeAnnotationNode = null);
    /// <summary>
    /// Index signature in interfaces: [key: string]: valueType, [key: number]: valueType, [key: symbol]: valueType
    /// </summary>
    public record IndexSignature(Token KeyName, TokenType KeyType, string ValueType, TypeNode? ValueTypeNode = null);
    /// <summary>
    /// Call signature in interfaces: (params): ReturnType or &lt;T&gt;(params): ReturnType
    /// Indicates the interface represents a callable type (e.g., function).
    /// </summary>
    /// <param name="TypeParams">Optional generic type parameters for this signature.</param>
    /// <param name="Parameters">The parameter list as raw parameter string.</param>
    /// <param name="ReturnType">The return type annotation.</param>
    /// <param name="ReturnTypeNode">Node twin of <paramref name="ReturnType"/> (type-AST migration), when the parser produced one.</param>
    public record CallSignature(List<TypeParam>? TypeParams, List<Parameter> Parameters, string ReturnType, TypeNode? ReturnTypeNode = null);
    /// <summary>
    /// Constructor signature in interfaces: new (params): ReturnType or new &lt;T&gt;(params): ReturnType
    /// Indicates the interface represents a constructable type.
    /// </summary>
    /// <param name="TypeParams">Optional generic type parameters for this signature.</param>
    /// <param name="Parameters">The parameter list as raw parameter string.</param>
    /// <param name="ReturnType">The return type annotation.</param>
    /// <param name="ReturnTypeNode">Node twin of <paramref name="ReturnType"/> (type-AST migration), when the parser produced one.</param>
    public record ConstructorSignature(List<TypeParam>? TypeParams, List<Parameter> Parameters, string ReturnType, TypeNode? ReturnTypeNode = null);
    public record Block(List<Stmt> Statements) : Stmt;
    public record Sequence(List<Stmt> Statements) : Stmt;  // Like Block but without creating a new scope
    public record Return(Token Keyword, Expr? Value) : Stmt;
    public record While(Expr Condition, Stmt Body) : Stmt;
    public record For(Stmt? Initializer, Expr? Condition, Expr? Increment, Stmt Body) : Stmt;
    public record DoWhile(Stmt Body, Expr Condition) : Stmt;
    public record ForOf(Token Variable, string? TypeAnnotation, Expr Iterable, Stmt Body, bool IsAsync = false) : Stmt;
    public record ForIn(Token Variable, string? TypeAnnotation, Expr Object, Stmt Body) : Stmt;
    public record If(Expr Condition, Stmt ThenBranch, Stmt? ElseBranch) : Stmt;
    public record Print(Expr Expr) : Stmt; // Temporary for console.log
    public record Break(Token Keyword, Token? Label = null) : Stmt;
    public record Continue(Token Keyword, Token? Label = null) : Stmt;
    /// <summary>
    /// Labeled statement: label: statement (allows break/continue to target by name)
    /// </summary>
    public record LabeledStatement(Token Label, Stmt Statement) : Stmt;
    public record SwitchCase(Expr Value, List<Stmt> Body);
    public record Switch(Expr Subject, List<SwitchCase> Cases, List<Stmt>? DefaultBody) : Stmt;
    // CatchParamType: optional catch-binding annotation text (`catch (e: unknown)`).
    // TS allows only 'any'/'unknown'; anything else is checker error TS1196.
    // CatchParamTypeNode: node twin of CatchParamType (type-AST migration), when the parser produced one.
    public record TryCatch(List<Stmt> TryBlock, Token? CatchParam, List<Stmt>? CatchBlock, List<Stmt>? FinallyBlock, string? CatchParamType = null, TypeNode? CatchParamTypeNode = null) : Stmt;
    public record Throw(Token Keyword, Expr Value) : Stmt;
    public record TypeAlias(Token Name, string TypeDefinition, List<TypeParam>? TypeParameters = null, TypeNode? TypeDefinitionNode = null) : Stmt;
    public record EnumMember(Token Name, Expr? Value);
    public record Enum(Token Name, List<EnumMember> Members, bool IsConst = false) : Stmt;

    /// <summary>
    /// Namespace declaration: namespace Name { members }
    /// Supports dotted names (A.B.C), which are desugared to nested namespaces during parsing.
    /// Members can include: classes, interfaces, functions, variables, enums, type aliases, nested namespaces.
    /// </summary>
    /// <param name="Name">The namespace name token</param>
    /// <param name="Members">List of member declarations</param>
    /// <param name="IsExported">Whether this namespace is exported from a module</param>
    public record Namespace(Token Name, List<Stmt> Members, bool IsExported = false) : Stmt;

    /// <summary>
    /// Import alias declaration: import X = Namespace.Member
    /// Creates a local alias for a namespace member (value or type).
    /// </summary>
    /// <param name="Keyword">The 'import' token for error reporting</param>
    /// <param name="AliasName">The local alias name (X)</param>
    /// <param name="QualifiedPath">The namespace path tokens [Namespace, Member]</param>
    /// <param name="IsExported">True if prefixed with 'export'</param>
    public record ImportAlias(
        Token Keyword,
        Token AliasName,
        List<Token> QualifiedPath,
        bool IsExported = false
    ) : Stmt;

    /// <summary>
    /// CommonJS-style import: import x = require('modulePath')
    /// Used for CommonJS interop and importing modules with export = syntax.
    /// </summary>
    /// <param name="Keyword">The 'import' token for error reporting</param>
    /// <param name="AliasName">The local alias name (x)</param>
    /// <param name="ModulePath">The module path string</param>
    /// <param name="IsExported">True if prefixed with 'export' (re-export)</param>
    public record ImportRequire(
        Token Keyword,
        Token AliasName,
        string ModulePath,
        bool IsExported = false
    ) : Stmt;

    // Module statements
    /// <summary>
    /// Import declaration: import { x, y } from './file', import Default from './file', etc.
    /// </summary>
    /// <param name="Keyword">The 'import' token for error reporting</param>
    /// <param name="NamedImports">Named imports: { x, y as z }</param>
    /// <param name="DefaultImport">Default import identifier</param>
    /// <param name="NamespaceImport">Namespace import: * as Module</param>
    /// <param name="ModulePath">Module path: './file' or 'lodash'</param>
    /// <param name="IsTypeOnly">True for 'import type ...' - type-only imports are erased at runtime</param>
    public record Import(
        Token Keyword,
        List<ImportSpecifier>? NamedImports,
        Token? DefaultImport,
        Token? NamespaceImport,
        string ModulePath,
        bool IsTypeOnly = false
    ) : Stmt;

    /// <summary>
    /// Individual import specifier: { x } or { x as y } or { type x }
    /// </summary>
    /// <param name="Imported">Original name in source module</param>
    /// <param name="LocalName">Renamed locally (null = same as imported)</param>
    /// <param name="IsTypeOnly">True for '{ type x }' - inline type-only specifier</param>
    public record ImportSpecifier(Token Imported, Token? LocalName, bool IsTypeOnly = false);

    /// <summary>
    /// Export declaration with various forms.
    /// </summary>
    /// <param name="Keyword">The 'export' token for error reporting</param>
    /// <param name="Declaration">Exported declaration: export function/class/const/let</param>
    /// <param name="NamedExports">Named exports: export { x, y as z }</param>
    /// <param name="DefaultExpr">Default export expression: export default expr</param>
    /// <param name="FromModulePath">Re-export source: export { x } from './file'</param>
    /// <param name="IsDefaultExport">True for 'export default'</param>
    /// <param name="ExportAssignment">CommonJS export assignment: export = expr</param>
    /// <param name="NamespaceExportName">Namespace re-export alias: export * as ns from './file'</param>
    public record Export(
        Token Keyword,
        Stmt? Declaration,
        List<ExportSpecifier>? NamedExports,
        Expr? DefaultExpr,
        string? FromModulePath,
        bool IsDefaultExport,
        Expr? ExportAssignment = null,
        Token? NamespaceExportName = null
    ) : Stmt;

    /// <summary>
    /// Individual export specifier: { x } or { x as y }
    /// </summary>
    /// <param name="LocalName">Name in current module</param>
    /// <param name="ExportedName">Exported as (null = same as local)</param>
    public record ExportSpecifier(Token LocalName, Token? ExportedName);

    /// <summary>
    /// File-level directive decorators (e.g., @Namespace("MyCompany.Libraries"))
    /// Applied to all types in the file during IL compilation.
    /// </summary>
    public record FileDirective(List<Decorator> Decorators) : Stmt;

    /// <summary>
    /// Directive prologue statement (e.g., "use strict").
    /// Directives are string literal statements at the beginning of a script or function body.
    /// </summary>
    /// <param name="Value">The directive value without quotes (e.g., "use strict")</param>
    /// <param name="StringToken">The original string token for error reporting</param>
    public record Directive(string Value, Token StringToken) : Stmt;

    /// <summary>
    /// Module augmentation or ambient module declaration: declare module 'path' { ... }
    /// </summary>
    /// <param name="Keyword">The 'declare' token for error reporting</param>
    /// <param name="ModulePath">Target module path string</param>
    /// <param name="Members">Declarations inside the block (interfaces, functions, vars, etc.)</param>
    /// <param name="IsAugmentation">True if augmenting existing module, false if ambient declaration</param>
    public record DeclareModule(
        Token Keyword,
        string ModulePath,
        List<Stmt> Members,
        bool IsAugmentation = false
    ) : Stmt;

    /// <summary>
    /// Global augmentation: declare global { ... }
    /// Allows adding declarations to the global scope from within a module.
    /// </summary>
    /// <param name="Keyword">The 'declare' token for error reporting</param>
    /// <param name="Members">Declarations to merge into global scope</param>
    public record DeclareGlobal(
        Token Keyword,
        List<Stmt> Members
    ) : Stmt;

    /// <summary>
    /// Single resource binding in a using declaration.
    /// Supports simple identifiers and destructuring patterns.
    /// </summary>
    /// <param name="Name">Variable name token (null for destructuring).</param>
    /// <param name="DestructuringPattern">ArrayDestructure or ObjectDestructure pattern (null for simple binding).</param>
    /// <param name="TypeAnnotation">Optional type annotation.</param>
    /// <param name="Initializer">Required initializer expression.</param>
    /// <param name="TypeAnnotationNode">Node twin of <paramref name="TypeAnnotation"/> (type-AST migration), when the parser produced one.</param>
    public record UsingBinding(
        Token? Name,
        Expr? DestructuringPattern,
        string? TypeAnnotation,
        Expr Initializer,
        TypeNode? TypeAnnotationNode = null
    );

    /// <summary>
    /// 'using' or 'await using' declaration for explicit resource management (TypeScript 5.2+).
    /// Resources are automatically disposed when the block scope exits.
    /// </summary>
    /// <param name="Keyword">The 'using' token for error reporting.</param>
    /// <param name="Bindings">One or more resource bindings.</param>
    /// <param name="IsAsync">True for 'await using', false for 'using'.</param>
    public record Using(
        Token Keyword,
        List<UsingBinding> Bindings,
        bool IsAsync
    ) : Stmt;
}
