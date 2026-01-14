namespace SharpTS.Parsing;

/// <summary>
/// Access modifier for class members.
/// </summary>
public enum AccessModifier { Public, Private, Protected }

/// <summary>
/// Type parameter in generic declarations (e.g., T in &lt;T extends Base&gt;).
/// </summary>
public record TypeParam(Token Name, string? Constraint);

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
    public record This(Token Keyword) : Expr;
    public record New(List<Token>? NamespacePath, Token ClassName, List<string>? TypeArgs, List<Expr> Arguments) : Expr;
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
    // Increment/decrement
    public record PrefixIncrement(Token Operator, Expr Operand) : Expr;
    public record PostfixIncrement(Expr Operand, Token Operator) : Expr;
    // Arrow function
    /// <summary>
    /// Arrow function expression. ThisType is for type annotations only (arrow expressions cannot have this parameter).
    /// IsObjectMethod indicates this is an object literal method shorthand, which binds 'this' to the object.
    /// IsAsync indicates this is an async arrow function that returns a Promise.
    /// </summary>
    public record ArrowFunction(List<TypeParam>? TypeParams, string? ThisType, List<Stmt.Parameter> Parameters, Expr? ExpressionBody, List<Stmt>? BlockBody, string? ReturnType, bool IsObjectMethod = false, bool IsAsync = false) : Expr;
    // Template literal
    public record TemplateLiteral(List<string> Strings, List<Expr> Expressions) : Expr;
    // Spread expression for calls and array literals
    public record Spread(Expr Expression) : Expr;
    // Type assertion: value as Type
    public record TypeAssertion(Expr Expression, string TargetType) : Expr;
    // Await expression: await expr (only valid inside async functions)
    public record Await(Token Keyword, Expr Expression) : Expr;
    // Dynamic import: import(pathExpr) - returns Promise of module namespace
    public record DynamicImport(Token Keyword, Expr PathExpression) : Expr;
    // Yield expression: yield expr or yield* expr (only valid inside generator functions)
    public record Yield(Token Keyword, Expr? Value, bool IsDelegating) : Expr;
    // Regex literal: /pattern/flags
    public record RegexLiteral(string Pattern, string Flags) : Expr;
    // Non-null assertion: expr! (asserts value is not null/undefined at compile time)
    public record NonNullAssertion(Expr Expression) : Expr;
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
    public record Var(Token Name, string? TypeAnnotation, Expr? Initializer) : Stmt;
    /// <summary>
    /// Function or method declaration. Body is null for overload signatures (declaration only).
    /// ThisType is the explicit this parameter type annotation (e.g., this: MyClass).
    /// IsAsync indicates this is an async function that returns a Promise.
    /// IsGenerator indicates this is a generator function (function*) that can yield values.
    /// Decorators contains any @decorator annotations applied to this function/method.
    /// </summary>
    public record Function(Token Name, List<TypeParam>? TypeParams, string? ThisType, List<Parameter> Parameters, List<Stmt>? Body, string? ReturnType, bool IsStatic = false, AccessModifier Access = AccessModifier.Public, bool IsAbstract = false, bool IsOverride = false, bool IsAsync = false, bool IsGenerator = false, List<Decorator>? Decorators = null) : Stmt;
    public record Parameter(Token Name, string? Type, Expr? DefaultValue = null, bool IsRest = false, bool IsParameterProperty = false, AccessModifier? Access = null, bool IsReadonly = false, bool IsOptional = false, List<Decorator>? Decorators = null);
    public record Field(Token Name, string? TypeAnnotation, Expr? Initializer, bool IsStatic = false, AccessModifier Access = AccessModifier.Public, bool IsReadonly = false, bool IsOptional = false, List<Decorator>? Decorators = null) : Stmt;
    public record Accessor(Token Name, Token Kind, Parameter? SetterParam, List<Stmt> Body, string? ReturnType, AccessModifier Access = AccessModifier.Public, bool IsAbstract = false, bool IsOverride = false, List<Decorator>? Decorators = null) : Stmt;
    /// <summary>
    /// Class declaration. IsDeclare indicates an ambient declaration (declare class) which has no implementation.
    /// </summary>
    public record Class(Token Name, List<TypeParam>? TypeParams, Token? Superclass, List<string>? SuperclassTypeArgs, List<Stmt.Function> Methods, List<Stmt.Field> Fields, List<Stmt.Accessor>? Accessors = null, List<Token>? Interfaces = null, List<List<string>>? InterfaceTypeArgs = null, bool IsAbstract = false, List<Decorator>? Decorators = null, bool IsDeclare = false) : Stmt;
    public record Interface(Token Name, List<TypeParam>? TypeParams, List<InterfaceMember> Members, List<IndexSignature>? IndexSignatures = null) : Stmt;
    public record InterfaceMember(Token Name, string Type, bool IsOptional = false);
    /// <summary>
    /// Index signature in interfaces: [key: string]: valueType, [key: number]: valueType, [key: symbol]: valueType
    /// </summary>
    public record IndexSignature(Token KeyName, TokenType KeyType, string ValueType);
    public record Block(List<Stmt> Statements) : Stmt;
    public record Sequence(List<Stmt> Statements) : Stmt;  // Like Block but without creating a new scope
    public record Return(Token Keyword, Expr? Value) : Stmt;
    public record While(Expr Condition, Stmt Body) : Stmt;
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

    // Module statements
    /// <summary>
    /// Import declaration: import { x, y } from './file', import Default from './file', etc.
    /// </summary>
    /// <param name="Keyword">The 'import' token for error reporting</param>
    /// <param name="NamedImports">Named imports: { x, y as z }</param>
    /// <param name="DefaultImport">Default import identifier</param>
    /// <param name="NamespaceImport">Namespace import: * as Module</param>
    /// <param name="ModulePath">Module path: './file' or 'lodash'</param>
    public record Import(
        Token Keyword,
        List<ImportSpecifier>? NamedImports,
        Token? DefaultImport,
        Token? NamespaceImport,
        string ModulePath
    ) : Stmt;

    /// <summary>
    /// Individual import specifier: { x } or { x as y }
    /// </summary>
    /// <param name="Imported">Original name in source module</param>
    /// <param name="LocalName">Renamed locally (null = same as imported)</param>
    public record ImportSpecifier(Token Imported, Token? LocalName);

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
}
