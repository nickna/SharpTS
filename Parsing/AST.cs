using SharpTS.Parsing.Visitors;
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
public record TypeParam(
    Token Name,
    string? Constraint,
    string? Default = null,
    bool IsConst = false,
    TypeParameterVariance Variance = TypeParameterVariance.Invariant
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
    /// <summary>
    /// Dispatches to the appropriate visitor method for the given expression's concrete type.
    /// Single source of truth for exhaustive expression dispatch.
    /// </summary>
    /// <typeparam name="TResult">The return type of the visitor methods.</typeparam>
    /// <param name="expr">The expression to dispatch.</param>
    /// <param name="visitor">The visitor implementation.</param>
    /// <returns>The result from the visitor method.</returns>
    /// <exception cref="InvalidOperationException">Thrown if an unknown Expr type is encountered.</exception>
    public static TResult Accept<TResult>(Expr expr, IExprVisitor<TResult> visitor) => expr switch
    {
        Binary e => visitor.VisitBinary(e),
        Logical e => visitor.VisitLogical(e),
        NullishCoalescing e => visitor.VisitNullishCoalescing(e),
        Ternary e => visitor.VisitTernary(e),
        Grouping e => visitor.VisitGrouping(e),
        Literal e => visitor.VisitLiteral(e),
        Unary e => visitor.VisitUnary(e),
        Delete e => visitor.VisitDelete(e),
        Variable e => visitor.VisitVariable(e),
        Assign e => visitor.VisitAssign(e),
        Call e => visitor.VisitCall(e),
        Get e => visitor.VisitGet(e),
        Set e => visitor.VisitSet(e),
        GetPrivate e => visitor.VisitGetPrivate(e),
        SetPrivate e => visitor.VisitSetPrivate(e),
        CallPrivate e => visitor.VisitCallPrivate(e),
        This e => visitor.VisitThis(e),
        New e => visitor.VisitNew(e),
        ArrayLiteral e => visitor.VisitArrayLiteral(e),
        ObjectLiteral e => visitor.VisitObjectLiteral(e),
        GetIndex e => visitor.VisitGetIndex(e),
        SetIndex e => visitor.VisitSetIndex(e),
        Super e => visitor.VisitSuper(e),
        CompoundAssign e => visitor.VisitCompoundAssign(e),
        CompoundSet e => visitor.VisitCompoundSet(e),
        CompoundSetIndex e => visitor.VisitCompoundSetIndex(e),
        LogicalAssign e => visitor.VisitLogicalAssign(e),
        LogicalSet e => visitor.VisitLogicalSet(e),
        LogicalSetIndex e => visitor.VisitLogicalSetIndex(e),
        PrefixIncrement e => visitor.VisitPrefixIncrement(e),
        PostfixIncrement e => visitor.VisitPostfixIncrement(e),
        ArrowFunction e => visitor.VisitArrowFunction(e),
        TemplateLiteral e => visitor.VisitTemplateLiteral(e),
        TaggedTemplateLiteral e => visitor.VisitTaggedTemplateLiteral(e),
        Spread e => visitor.VisitSpread(e),
        TypeAssertion e => visitor.VisitTypeAssertion(e),
        Satisfies e => visitor.VisitSatisfies(e),
        Await e => visitor.VisitAwait(e),
        DynamicImport e => visitor.VisitDynamicImport(e),
        ImportMeta e => visitor.VisitImportMeta(e),
        Yield e => visitor.VisitYield(e),
        RegexLiteral e => visitor.VisitRegexLiteral(e),
        NonNullAssertion e => visitor.VisitNonNullAssertion(e),
        ClassExpr e => visitor.VisitClassExpr(e),
        _ => throw new InvalidOperationException($"Unknown Expr type: {expr.GetType().Name}")
    };

    public record Binary(Expr Left, Token Operator, Expr Right) : Expr;
    public record Logical(Expr Left, Token Operator, Expr Right) : Expr;
    public record NullishCoalescing(Expr Left, Expr Right) : Expr;
    public record Ternary(Expr Condition, Expr ThenBranch, Expr ElseBranch) : Expr;
    public record Grouping(Expr Expression) : Expr;
    public record Literal(object? Value) : Expr;
    public record Unary(Token Operator, Expr Right) : Expr;
    public record Delete(Token Keyword, Expr Operand) : Expr;
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
    public record Property(
        PropertyKey? Key,
        Expr Value,
        bool IsSpread = false,
        ObjectPropertyKind Kind = ObjectPropertyKind.Value,
        Stmt.Parameter? SetterParam = null);
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
        bool IsAbstract = false,
        List<Stmt>? StaticInitializers = null
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
    /// <summary>
    /// Dispatches to the appropriate visitor method for the given statement's concrete type.
    /// Single source of truth for exhaustive statement dispatch.
    /// </summary>
    /// <typeparam name="TResult">The return type of the visitor methods.</typeparam>
    /// <param name="stmt">The statement to dispatch.</param>
    /// <param name="visitor">The visitor implementation.</param>
    /// <returns>The result from the visitor method.</returns>
    /// <exception cref="InvalidOperationException">Thrown if an unknown Stmt type is encountered.</exception>
    public static TResult Accept<TResult>(Stmt stmt, IStmtVisitor<TResult> visitor) => stmt switch
    {
        Expression s => visitor.VisitExpression(s),
        Var s => visitor.VisitVar(s),
        Const s => visitor.VisitConst(s),
        Function s => visitor.VisitFunction(s),
        Field s => visitor.VisitField(s),
        Accessor s => visitor.VisitAccessor(s),
        AutoAccessor s => visitor.VisitAutoAccessor(s),
        Class s => visitor.VisitClass(s),
        StaticBlock s => visitor.VisitStaticBlock(s),
        Interface s => visitor.VisitInterface(s),
        Block s => visitor.VisitBlock(s),
        Sequence s => visitor.VisitSequence(s),
        Return s => visitor.VisitReturn(s),
        While s => visitor.VisitWhile(s),
        For s => visitor.VisitFor(s),
        DoWhile s => visitor.VisitDoWhile(s),
        ForOf s => visitor.VisitForOf(s),
        ForIn s => visitor.VisitForIn(s),
        If s => visitor.VisitIf(s),
        Print s => visitor.VisitPrint(s),
        Break s => visitor.VisitBreak(s),
        Continue s => visitor.VisitContinue(s),
        LabeledStatement s => visitor.VisitLabeledStatement(s),
        Switch s => visitor.VisitSwitch(s),
        TryCatch s => visitor.VisitTryCatch(s),
        Throw s => visitor.VisitThrow(s),
        TypeAlias s => visitor.VisitTypeAlias(s),
        Enum s => visitor.VisitEnum(s),
        Namespace s => visitor.VisitNamespace(s),
        ImportAlias s => visitor.VisitImportAlias(s),
        ImportRequire s => visitor.VisitImportRequire(s),
        Import s => visitor.VisitImport(s),
        Export s => visitor.VisitExport(s),
        FileDirective s => visitor.VisitFileDirective(s),
        Directive s => visitor.VisitDirective(s),
        DeclareModule s => visitor.VisitDeclareModule(s),
        DeclareGlobal s => visitor.VisitDeclareGlobal(s),
        Using s => visitor.VisitUsing(s),
        _ => throw new InvalidOperationException($"Unknown Stmt type: {stmt.GetType().Name}")
    };

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
    /// <summary>
    /// Class field declaration. For computed property names (e.g., [Symbol("key")]: type),
    /// ComputedKey contains the expression and Name is a synthetic token.
    /// </summary>
    public record Field(Token Name, string? TypeAnnotation, Expr? Initializer, bool IsStatic = false, AccessModifier Access = AccessModifier.Public, bool IsReadonly = false, bool IsOptional = false, bool HasDefiniteAssignmentAssertion = false, List<Decorator>? Decorators = null, bool IsPrivate = false, bool IsDeclare = false, Expr? ComputedKey = null) : Stmt;
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
    /// StaticInitializers contains static fields and static blocks in declaration order for proper initialization sequencing.
    /// </summary>
    public record Class(Token Name, List<TypeParam>? TypeParams, Token? Superclass, List<string>? SuperclassTypeArgs, List<Stmt.Function> Methods, List<Stmt.Field> Fields, List<Stmt.Accessor>? Accessors = null, List<Stmt.AutoAccessor>? AutoAccessors = null, List<Token>? Interfaces = null, List<List<string>>? InterfaceTypeArgs = null, bool IsAbstract = false, List<Decorator>? Decorators = null, bool IsDeclare = false, List<Stmt>? StaticInitializers = null) : Stmt;
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
        List<ConstructorSignature>? ConstructorSignatures = null
    ) : Stmt;
    public record InterfaceMember(Token Name, string Type, bool IsOptional = false, bool IsReadonly = false);
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
    public record Export(
        Token Keyword,
        Stmt? Declaration,
        List<ExportSpecifier>? NamedExports,
        Expr? DefaultExpr,
        string? FromModulePath,
        bool IsDefaultExport,
        Expr? ExportAssignment = null
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
    public record UsingBinding(
        Token? Name,
        Expr? DestructuringPattern,
        string? TypeAnnotation,
        Expr Initializer
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
