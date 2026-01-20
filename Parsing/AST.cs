namespace SharpTS.Parsing;

/// <summary>
/// Access modifier for class members.
/// </summary>
public enum AccessModifier { Public, Private, Protected }

/// <summary>
/// Type parameter in generic declarations (e.g., T in &lt;T extends Base&gt; or &lt;T = string&gt;).
/// </summary>
/// <param name="Name">The type parameter identifier token.</param>
/// <param name="Constraint">Optional constraint type (after extends keyword).</param>
/// <param name="Default">Optional default type (after = sign).</param>
public record TypeParam(Token Name, string? Constraint, string? Default = null);

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
    public record Binary(Expr Left, Token Operator, Expr Right) : Expr;
    public record Logical(Expr Left, Token Operator, Expr Right) : Expr;
    public record NullishCoalescing(Expr Left, Expr Right) : Expr;
    public record Ternary(Expr Condition, Expr ThenBranch, Expr ElseBranch) : Expr;
    public record Grouping(Expr Expression) : Expr;
    public record Literal(object? Value) : Expr;
    public record Unary(Token Operator, Expr Right) : Expr;
    public record Variable(Token Name) : Expr;
    public record Assign(Token Name, Expr Value) : Expr;
    public record Call(Expr Callee, Token Paren, List<string>? TypeArgs, List<Expr> Arguments) : Expr;
    public record Get(Expr Object, Token Name, bool Optional = false) : Expr;
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
    /// </summary>
    public record New(Expr Callee, List<string>? TypeArgs, List<Expr> Arguments) : Expr;
    public record ArrayLiteral(List<Expr> Elements) : Expr;
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
    public record Property(PropertyKey? Key, Expr Value, bool IsSpread = false);
    public record GetIndex(Expr Object, Expr Index) : Expr;
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
    /// </summary>
    public record ArrowFunction(Token? Name, List<TypeParam>? TypeParams, string? ThisType, List<Stmt.Parameter> Parameters, Expr? ExpressionBody, List<Stmt>? BlockBody, string? ReturnType, bool HasOwnThis = false, bool IsAsync = false, bool IsGenerator = false) : Expr;
    // Template literal
    public record TemplateLiteral(List<string> Strings, List<Expr> Expressions) : Expr;
    // Tagged template literal: tag`template ${expr}`
    public record TaggedTemplateLiteral(
        Expr Tag,                     // The tag function expression
        List<string?> CookedStrings,  // Processed escapes (null for invalid)
        List<string> RawStrings,      // Literal text (unprocessed)
        List<Expr> Expressions        // Interpolated expressions
    ) : Expr;
    // Spread expression for calls and array literals
    public record Spread(Expr Expression) : Expr;
    // Type assertion: value as Type
    public record TypeAssertion(Expr Expression, string TargetType) : Expr;
    // Satisfies operator: value satisfies Type (TS 4.9+) - validates without widening
    public record Satisfies(Expr Expression, string ConstraintType) : Expr;
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
        Token? Superclass,
        List<string>? SuperclassTypeArgs,
        List<Stmt.Function> Methods,
        List<Stmt.Field> Fields,
        List<Stmt.Accessor>? Accessors = null,
        List<Stmt.AutoAccessor>? AutoAccessors = null,
        List<Token>? Interfaces = null,
        List<List<string>>? InterfaceTypeArgs = null,
        bool IsAbstract = false
    ) : Expr;
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
    public record Var(Token Name, string? TypeAnnotation, Expr? Initializer, bool HasDefiniteAssignmentAssertion = false) : Stmt;
    /// <summary>
    /// Const variable declaration. Separate from Var for cleaner const-specific handling (e.g., unique symbol).
    /// Initializer is non-nullable since const always requires initialization.
    /// </summary>
    public record Const(Token Name, string? TypeAnnotation, Expr Initializer) : Stmt;
    /// <summary>
    /// Function or method declaration. Body is null for overload signatures (declaration only).
    /// ThisType is the explicit this parameter type annotation (e.g., this: MyClass).
    /// IsAsync indicates this is an async function that returns a Promise.
    /// IsGenerator indicates this is a generator function (function*) that can yield values.
    /// Decorators contains any @decorator annotations applied to this function/method.
    /// </summary>
    public record Function(Token Name, List<TypeParam>? TypeParams, string? ThisType, List<Parameter> Parameters, List<Stmt>? Body, string? ReturnType, bool IsStatic = false, AccessModifier Access = AccessModifier.Public, bool IsAbstract = false, bool IsOverride = false, bool IsAsync = false, bool IsGenerator = false, List<Decorator>? Decorators = null, bool IsPrivate = false) : Stmt;
    public record Parameter(Token Name, string? Type, Expr? DefaultValue = null, bool IsRest = false, bool IsParameterProperty = false, AccessModifier? Access = null, bool IsReadonly = false, bool IsOptional = false, List<Decorator>? Decorators = null);
    public record Field(Token Name, string? TypeAnnotation, Expr? Initializer, bool IsStatic = false, AccessModifier Access = AccessModifier.Public, bool IsReadonly = false, bool IsOptional = false, bool HasDefiniteAssignmentAssertion = false, List<Decorator>? Decorators = null, bool IsPrivate = false) : Stmt;
    public record Accessor(Token Name, Token Kind, Parameter? SetterParam, List<Stmt> Body, string? ReturnType, AccessModifier Access = AccessModifier.Public, bool IsAbstract = false, bool IsOverride = false, List<Decorator>? Decorators = null) : Stmt;
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
    public record AutoAccessor(
        Token Name,
        string? TypeAnnotation,
        Expr? Initializer,
        bool IsStatic = false,
        AccessModifier Access = AccessModifier.Public,
        bool IsReadonly = false,
        bool IsOverride = false,
        List<Decorator>? Decorators = null
    ) : Stmt;
    /// <summary>
    /// Class declaration. IsDeclare indicates an ambient declaration (declare class) which has no implementation.
    /// </summary>
    public record Class(Token Name, List<TypeParam>? TypeParams, Token? Superclass, List<string>? SuperclassTypeArgs, List<Stmt.Function> Methods, List<Stmt.Field> Fields, List<Stmt.Accessor>? Accessors = null, List<Stmt.AutoAccessor>? AutoAccessors = null, List<Token>? Interfaces = null, List<List<string>>? InterfaceTypeArgs = null, bool IsAbstract = false, List<Decorator>? Decorators = null, bool IsDeclare = false) : Stmt;
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
        List<ConstructorSignature>? ConstructorSignatures = null
    ) : Stmt;
    public record InterfaceMember(Token Name, string Type, bool IsOptional = false);
    /// <summary>
    /// Index signature in interfaces: [key: string]: valueType, [key: number]: valueType, [key: symbol]: valueType
    /// </summary>
    public record IndexSignature(Token KeyName, TokenType KeyType, string ValueType);
    /// <summary>
    /// Call signature in interfaces: (params): ReturnType or &lt;T&gt;(params): ReturnType
    /// Indicates the interface represents a callable type (e.g., function).
    /// </summary>
    /// <param name="TypeParams">Optional generic type parameters for this signature.</param>
    /// <param name="Parameters">The parameter list as raw parameter string.</param>
    /// <param name="ReturnType">The return type annotation.</param>
    public record CallSignature(List<TypeParam>? TypeParams, List<Parameter> Parameters, string ReturnType);
    /// <summary>
    /// Constructor signature in interfaces: new (params): ReturnType or new &lt;T&gt;(params): ReturnType
    /// Indicates the interface represents a constructable type.
    /// </summary>
    /// <param name="TypeParams">Optional generic type parameters for this signature.</param>
    /// <param name="Parameters">The parameter list as raw parameter string.</param>
    /// <param name="ReturnType">The return type annotation.</param>
    public record ConstructorSignature(List<TypeParam>? TypeParams, List<Parameter> Parameters, string ReturnType);
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
    public record TryCatch(List<Stmt> TryBlock, Token? CatchParam, List<Stmt>? CatchBlock, List<Stmt>? FinallyBlock) : Stmt;
    public record Throw(Token Keyword, Expr Value) : Stmt;
    public record TypeAlias(Token Name, string TypeDefinition, List<TypeParam>? TypeParameters = null) : Stmt;
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
    public record Export(
        Token Keyword,
        Stmt? Declaration,
        List<ExportSpecifier>? NamedExports,
        Expr? DefaultExpr,
        string? FromModulePath,
        bool IsDefaultExport
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
}
