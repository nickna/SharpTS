namespace SharpTS.Parsing;

public partial class Parser
{
    // ============== MODULE PARSING ==============

    /// <summary>
    /// Parses import declarations:
    /// - import './module';                          (side-effect import)
    /// - import { x, y } from './module';           (named imports)
    /// - import { x as alias } from './module';     (aliased imports)
    /// - import Default from './module';            (default import)
    /// - import * as Module from './module';        (namespace import)
    /// - import Default, { x, y } from './module';  (combined)
    /// - import type { x } from './module';         (type-only import)
    /// - import { type x, y } from './module';      (inline type specifiers)
    /// </summary>
    private Stmt ImportDeclaration()
    {
        Token keyword = Previous();

        // Check for 'import type' (statement-level type-only import)
        bool isTypeOnlyImport = Match(TokenType.TYPE);

        // import './module' (side-effect import)
        if (Check(TokenType.STRING))
        {
            string path = (string)Consume(TokenType.STRING, "Expect module path.").Literal!;
            ConsumeSemicolon("Expect ';' after import.");
            return new Stmt.Import(keyword, null, null, null, path, isTypeOnlyImport);
        }

        Token? defaultImport = null;
        Token? namespaceImport = null;
        List<Stmt.ImportSpecifier>? namedImports = null;

        // import * as Namespace from './module'
        if (Match(TokenType.STAR))
        {
            Consume(TokenType.AS, "Expect 'as' after '*'.");
            namespaceImport = Consume(TokenType.IDENTIFIER, "Expect namespace name.");
        }
        // import { x, y } or import Default or import Default, { x, y }
        else if (Check(TokenType.IDENTIFIER))
        {
            defaultImport = Advance();

            // Check for combined: import Default, { named } or import Default, * as NS
            if (Match(TokenType.COMMA))
            {
                if (Match(TokenType.STAR))
                {
                    Consume(TokenType.AS, "Expect 'as' after '*'.");
                    namespaceImport = Consume(TokenType.IDENTIFIER, "Expect namespace name.");
                }
                else if (Match(TokenType.LEFT_BRACE))
                {
                    namedImports = ParseImportSpecifiers();
                }
                else
                {
                    throw new Exception($"Line {Peek().Line}: Expect '{{' or '*' after ',' in import.");
                }
            }
        }
        else if (Match(TokenType.LEFT_BRACE))
        {
            namedImports = ParseImportSpecifiers();
        }
        else
        {
            throw new Exception($"Line {Peek().Line}: Expect import specifiers.");
        }

        Consume(TokenType.FROM, "Expect 'from' after import specifiers.");
        string modulePath = (string)Consume(TokenType.STRING, "Expect module path string.").Literal!;
        ConsumeSemicolon("Expect ';' after import declaration.");

        return new Stmt.Import(keyword, namedImports, defaultImport, namespaceImport, modulePath, isTypeOnlyImport);
    }

    /// <summary>
    /// Parses the list of import specifiers inside { }.
    /// Supports inline type specifiers: { type Foo, bar }
    /// </summary>
    private List<Stmt.ImportSpecifier> ParseImportSpecifiers()
    {
        // Already consumed LEFT_BRACE
        List<Stmt.ImportSpecifier> specifiers = [];

        if (!Check(TokenType.RIGHT_BRACE))
        {
            do
            {
                // Check for inline type specifier: { type Foo }.
                // But `type` is also a valid identifier (e.g. `{ type as myType }`),
                // so only consume it as the modifier when the following token would
                // begin a new specifier — not `as`, `,`, or `}`.
                bool isTypeOnly = false;
                if (Check(TokenType.TYPE))
                {
                    var nextType = PeekNext().Type;
                    if (nextType != TokenType.AS && nextType != TokenType.COMMA && nextType != TokenType.RIGHT_BRACE)
                    {
                        Advance();
                        isTypeOnly = true;
                    }
                }

                Token imported = ConsumeSpecifierName("Expect import name.");
                Token? localName = null;

                if (Match(TokenType.AS))
                {
                    localName = Consume(TokenType.IDENTIFIER, "Expect local name after 'as'.");
                }

                specifiers.Add(new Stmt.ImportSpecifier(imported, localName, isTypeOnly));
                // Trailing comma before '}' is allowed: { a, b, }
                if (!Match(TokenType.COMMA)) break;
            } while (!Check(TokenType.RIGHT_BRACE));
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after import specifiers.");
        return specifiers;
    }

    /// <summary>
    /// Parses after seeing 'import IDENTIFIER ='.
    /// Distinguishes: import X = require('path') vs import X = Namespace.Member
    /// </summary>
    /// <param name="isExported">True if prefixed with 'export'</param>
    private Stmt ParseImportWithEquals(bool isExported)
    {
        Token keyword = Previous(); // 'import' token

        // Parse alias name
        Token aliasName = Consume(TokenType.IDENTIFIER, "Expect alias name after 'import'.");

        // Consume '='
        Consume(TokenType.EQUAL, "Expect '=' after alias name in import alias.");

        // Check for require('path')
        if (Check(TokenType.IDENTIFIER) && Peek().Lexeme == "require")
        {
            Advance(); // consume 'require'
            Consume(TokenType.LEFT_PAREN, "Expect '(' after 'require'.");
            string modulePath = (string)Consume(TokenType.STRING, "Expect module path string in require().").Literal!;
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after module path.");
            ConsumeSemicolon("Expect ';' after import require.");
            return new Stmt.ImportRequire(keyword, aliasName, modulePath, isExported);
        }

        // Otherwise namespace alias: import X = Namespace.Member
        return ImportAliasDeclarationAfterEquals(keyword, aliasName, isExported);
    }

    /// <summary>
    /// Parses import alias declaration after the '=' has been consumed: A.B.C.member;
    /// </summary>
    /// <param name="keyword">The 'import' token for error reporting</param>
    /// <param name="aliasName">The alias name token</param>
    /// <param name="isExported">True if prefixed with 'export'</param>
    private Stmt ImportAliasDeclarationAfterEquals(Token keyword, Token aliasName, bool isExported)
    {
        // Parse qualified path: A.B.C.member
        List<Token> path = [Consume(TokenType.IDENTIFIER, "Expect namespace path after '='.")];

        while (Match(TokenType.DOT))
        {
            path.Add(Consume(TokenType.IDENTIFIER, "Expect identifier after '.' in namespace path."));
        }

        // Path must have at least 2 parts (Namespace.member)
        if (path.Count < 2)
        {
            throw new Exception($"Parse Error at line {keyword.Line}: Import alias path must have at least two parts (e.g., Namespace.Member).");
        }

        ConsumeSemicolon("Expect ';' after import alias.");

        return new Stmt.ImportAlias(keyword, aliasName, path, isExported);
    }

    /// <summary>
    /// Legacy entry point for import alias declaration (for backward compatibility).
    /// Redirects to ParseImportWithEquals which handles both require() and namespace alias.
    /// </summary>
    /// <param name="isExported">True if prefixed with 'export'</param>
    private Stmt ImportAliasDeclaration(bool isExported)
    {
        return ParseImportWithEquals(isExported);
    }

    /// <summary>
    /// Parses export declarations:
    /// - export const x = 5;                        (declaration export)
    /// - export function foo() {}                   (function export)
    /// - export class MyClass {}                    (class export)
    /// - export { x, y };                           (named exports)
    /// - export { x as alias };                     (aliased exports)
    /// - export { x } from './module';              (re-export)
    /// - export * from './module';                  (re-export all)
    /// - export default expression;                 (default export)
    /// - export default class {}                    (default class export)
    /// - export = expression;                       (CommonJS export assignment)
    /// </summary>
    /// <param name="classDecorators">
    /// Decorators parsed before the `export` keyword (`@decorator export class …`). Only the
    /// class-producing forms — `export [default|abstract] class` and `export declare [abstract]
    /// class` — accept them; every other export form rejects them via <see cref="RejectDecorators"/>,
    /// matching TypeScript's "decorators can only be applied to classes and class members" rule.
    /// </param>
    private Stmt ExportDeclaration(List<Decorator>? classDecorators = null)
    {
        Token keyword = Previous();

        // Decorators are only valid on the class-producing export forms below. Every other
        // branch calls this first so `@dec export function/const/{…}/…` reports the same
        // "not valid here" error TypeScript does, instead of silently dropping the decorators.
        void RejectDecorators()
        {
            if (classDecorators is { Count: > 0 })
            {
                throw new Exception($"Parse Error at line {classDecorators[0].AtToken.Line}: Decorators are not valid here. Decorators can only be applied to classes and class members.");
            }
        }

        // export = <expression> (CommonJS export assignment)
        if (Match(TokenType.EQUAL))
        {
            RejectDecorators();
            Expr exportValue = Expression();
            ConsumeSemicolon("Expect ';' after export assignment.");
            return new Stmt.Export(keyword, null, null, null, null, false, exportValue);
        }

        // export default ...
        if (Match(TokenType.DEFAULT))
        {
            Expr? defaultExpr = null;
            Stmt? declaration = null;

            // export default class Name { }
            // export default function name() { } / function* name() { }
            // export default async function name() { } / async function* name() { }
            if (Match(TokenType.CLASS))
            {
                declaration = ClassDeclaration(isAbstract: false, classDecorators: classDecorators);
            }
            else if (Check(TokenType.ASYNC) && PeekNext().Type == TokenType.FUNCTION)
            {
                // `export default async function ...` — only claim ASYNC here when a
                // `function` follows, so `export default async () => {}` still parses
                // as a default async-arrow expression below.
                RejectDecorators();
                Advance(); // consume 'async'
                Advance(); // consume 'function'
                bool isGenerator = Match(TokenType.STAR);
                declaration = FunctionDeclaration("function", isAsync: true, isGenerator: isGenerator);
            }
            else if (Match(TokenType.FUNCTION))
            {
                RejectDecorators();
                bool isGenerator = Match(TokenType.STAR);
                declaration = FunctionDeclaration("function", isAsync: false, isGenerator: isGenerator);
            }
            else
            {
                // export default <expression>;
                RejectDecorators();
                defaultExpr = Expression();
                ConsumeSemicolon("Expect ';' after export default expression.");
            }

            return new Stmt.Export(keyword, declaration, null, defaultExpr, null, IsDefaultExport: true);
        }

        // export { x, y } or export { x } from './module'
        if (Match(TokenType.LEFT_BRACE))
        {
            RejectDecorators();
            var namedExports = ParseExportSpecifiers();

            // Re-export: export { x } from './module'
            string? fromPath = null;
            if (Match(TokenType.FROM))
            {
                fromPath = (string)Consume(TokenType.STRING, "Expect module path.").Literal!;
            }

            ConsumeSemicolon("Expect ';' after export.");
            return new Stmt.Export(keyword, null, namedExports, null, fromPath, IsDefaultExport: false);
        }

        // export * from './module' (re-export all) or
        // export * as ns from './module' (re-export the whole module as a named namespace)
        if (Match(TokenType.STAR))
        {
            RejectDecorators();
            Token? namespaceExportName = null;
            if (Match(TokenType.AS))
            {
                namespaceExportName = ConsumeIdentifierName("Expect namespace name after 'as'.");
            }
            Consume(TokenType.FROM, "Expect 'from' after '*'.");
            string fromPath = (string)Consume(TokenType.STRING, "Expect module path.").Literal!;
            ConsumeSemicolon("Expect ';' after export.");

            // Represent as export with null named exports and a fromPath (meaning all)
            return new Stmt.Export(keyword, null, null, null, fromPath, IsDefaultExport: false,
                NamespaceExportName: namespaceExportName);
        }

        // export import X = Namespace.Member (re-export alias)
        // export import X = require('path') (re-export require)
        if (Match(TokenType.IMPORT))
        {
            RejectDecorators();
            if (Check(TokenType.IDENTIFIER) && PeekNext().Type == TokenType.EQUAL)
            {
                return ParseImportWithEquals(isExported: true);
            }
            throw new Exception($"Parse Error at line {Peek().Line}: Expected import alias after 'export import' (e.g., 'export import X = Namespace.Member' or 'export import X = require(\"...\")')).");
        }

        // export function/class/const/let/interface/type/enum
        Stmt? decl = null;
        if (Match(TokenType.ASYNC))
        {
            // export async function foo() {}  /  export async function* foo() {}
            RejectDecorators();
            Consume(TokenType.FUNCTION, "Expect 'function' after 'async'.");
            bool isGenerator = Match(TokenType.STAR);
            decl = FunctionDeclaration("function", isAsync: true, isGenerator: isGenerator);
        }
        else if (Match(TokenType.FUNCTION))
        {
            // export function foo() {}  /  export function* foo() {}
            RejectDecorators();
            bool isGenerator = Match(TokenType.STAR);
            decl = FunctionDeclaration("function", isAsync: false, isGenerator: isGenerator);
        }
        else if (Match(TokenType.CLASS))
        {
            decl = ClassDeclaration(isAbstract: false, classDecorators: classDecorators);
        }
        else if (Match(TokenType.ABSTRACT))
        {
            Consume(TokenType.CLASS, "Expect 'class' after 'abstract'.");
            decl = ClassDeclaration(isAbstract: true, classDecorators: classDecorators);
        }
        else if (Match(TokenType.CONST))
        {
            RejectDecorators();
            if (Match(TokenType.ENUM))
            {
                decl = EnumDeclaration(isConst: true);
            }
            else
            {
                // Pass isConst:true so `export const x = 5` produces a Stmt.Const, matching
                // the bare-`const` path (Parser.Declarations.cs). Without it the declaration
                // was a mutable Stmt.Var — reassignment went unflagged and the literal type was
                // widened (`export const x = 5` inferred `number` instead of `5`). See #428.
                decl = VarDeclaration(isConst: true);
            }
        }
        else if (Match(TokenType.LET))
        {
            RejectDecorators();
            decl = VarDeclaration();
        }
        else if (Match(TokenType.VAR))
        {
            RejectDecorators();
            // IsVar so the declaration participates in var hoisting (VarHoister).
            decl = VarDeclaration(isConst: false, isVar: true);
        }
        else if (Match(TokenType.INTERFACE))
        {
            RejectDecorators();
            decl = InterfaceDeclaration();
        }
        else if (Match(TokenType.TYPE))
        {
            RejectDecorators();
            decl = TypeAliasDeclaration();
        }
        else if (Match(TokenType.ENUM))
        {
            RejectDecorators();
            decl = EnumDeclaration(isConst: false);
        }
        else if (Match(TokenType.NAMESPACE))
        {
            RejectDecorators();
            decl = NamespaceDeclaration(isExported: true);
        }
        else if (Match(TokenType.DECLARE))
        {
            // `export declare [abstract] class …` — an exported ambient class declaration
            // (the form `--gen-decl` and docs/dotnet-types.md emit, e.g.
            // `@DotNetType(...) export declare class X {}`). Handle the class forms here,
            // mirroring the bare `declare class` path in Declaration(), so decorators reach
            // the class body; other ambient forms go through AmbientDeclaration() and reject
            // decorators.
            if (Match(TokenType.ABSTRACT))
            {
                Consume(TokenType.CLASS, "Expect 'class' after 'declare abstract'.");
                decl = ClassDeclaration(isAbstract: true, classDecorators: classDecorators, isDeclare: true);
            }
            else if (Match(TokenType.CLASS))
            {
                decl = ClassDeclaration(isAbstract: false, classDecorators: classDecorators, isDeclare: true);
            }
            else
            {
                // `export declare function/const/…` — a non-class exported ambient declaration.
                RejectDecorators();
                decl = AmbientDeclaration();
            }
        }
        else
        {
            throw new Exception($"Line {Peek().Line}: Expect declaration after 'export'.");
        }

        return new Stmt.Export(keyword, decl, null, null, null, IsDefaultExport: false);
    }

    /// <summary>
    /// Parses the list of export specifiers inside { }.
    /// </summary>
    private List<Stmt.ExportSpecifier> ParseExportSpecifiers()
    {
        // Already consumed LEFT_BRACE
        List<Stmt.ExportSpecifier> specifiers = [];

        if (!Check(TokenType.RIGHT_BRACE))
        {
            do
            {
                Token localName = ConsumeSpecifierName("Expect export name.");
                Token? exportedName = null;

                if (Match(TokenType.AS))
                {
                    exportedName = ConsumeIdentifierName("Expect exported name after 'as'.");
                }

                specifiers.Add(new Stmt.ExportSpecifier(localName, exportedName));
                if (!Match(TokenType.COMMA)) break;
            } while (!Check(TokenType.RIGHT_BRACE));
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after export specifiers.");
        return specifiers;
    }

    /// <summary>
    /// Consumes a specifier name in import/export braces. Accepts identifiers, the
    /// <c>default</c> keyword (per ECMAScript spec), and TypeScript contextual
    /// keywords like <c>type</c>, <c>from</c>, <c>of</c> — any of which may appear
    /// as a real export name (e.g., <c>export { type }</c>, <c>import { type as t }</c>).
    /// </summary>
    private Token ConsumeSpecifierName(string errorMessage)
    {
        if (Check(TokenType.IDENTIFIER) || Check(TokenType.DEFAULT))
            return Advance();
        if (IsContextualKeyword(Peek().Type))
        {
            var token = Advance();
            return new Token(TokenType.IDENTIFIER, token.Lexeme, null, token.Line);
        }
        throw new Exception($"Parse Error at line {Peek().Line}: {errorMessage}");
    }
}
