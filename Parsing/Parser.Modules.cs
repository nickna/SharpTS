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
    /// </summary>
    private Stmt ImportDeclaration()
    {
        Token keyword = Previous();

        // import './module' (side-effect import)
        if (Check(TokenType.STRING))
        {
            string path = (string)Consume(TokenType.STRING, "Expect module path.").Literal!;
            Consume(TokenType.SEMICOLON, "Expect ';' after import.");
            return new Stmt.Import(keyword, null, null, null, path);
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
        Consume(TokenType.SEMICOLON, "Expect ';' after import declaration.");

        return new Stmt.Import(keyword, namedImports, defaultImport, namespaceImport, modulePath);
    }

    /// <summary>
    /// Parses the list of import specifiers inside { }.
    /// </summary>
    private List<Stmt.ImportSpecifier> ParseImportSpecifiers()
    {
        // Already consumed LEFT_BRACE
        List<Stmt.ImportSpecifier> specifiers = [];

        if (!Check(TokenType.RIGHT_BRACE))
        {
            do
            {
                Token imported = Consume(TokenType.IDENTIFIER, "Expect import name.");
                Token? localName = null;

                if (Match(TokenType.AS))
                {
                    localName = Consume(TokenType.IDENTIFIER, "Expect local name after 'as'.");
                }

                specifiers.Add(new Stmt.ImportSpecifier(imported, localName));
            } while (Match(TokenType.COMMA));
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after import specifiers.");
        return specifiers;
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
    /// </summary>
    private Stmt ExportDeclaration()
    {
        Token keyword = Previous();

        // export default ...
        if (Match(TokenType.DEFAULT))
        {
            Expr? defaultExpr = null;
            Stmt? declaration = null;

            // export default class Name { } or export default function name() { }
            if (Match(TokenType.CLASS))
            {
                declaration = ClassDeclaration(isAbstract: false);
            }
            else if (Match(TokenType.FUNCTION))
            {
                declaration = FunctionDeclaration("function");
            }
            else
            {
                // export default <expression>;
                defaultExpr = Expression();
                Consume(TokenType.SEMICOLON, "Expect ';' after export default expression.");
            }

            return new Stmt.Export(keyword, declaration, null, defaultExpr, null, IsDefaultExport: true);
        }

        // export { x, y } or export { x } from './module'
        if (Match(TokenType.LEFT_BRACE))
        {
            var namedExports = ParseExportSpecifiers();

            // Re-export: export { x } from './module'
            string? fromPath = null;
            if (Match(TokenType.FROM))
            {
                fromPath = (string)Consume(TokenType.STRING, "Expect module path.").Literal!;
            }

            Consume(TokenType.SEMICOLON, "Expect ';' after export.");
            return new Stmt.Export(keyword, null, namedExports, null, fromPath, IsDefaultExport: false);
        }

        // export * from './module' (re-export all)
        if (Match(TokenType.STAR))
        {
            Consume(TokenType.FROM, "Expect 'from' after '*'.");
            string fromPath = (string)Consume(TokenType.STRING, "Expect module path.").Literal!;
            Consume(TokenType.SEMICOLON, "Expect ';' after export.");

            // Represent as export with null named exports and a fromPath (meaning all)
            return new Stmt.Export(keyword, null, null, null, fromPath, IsDefaultExport: false);
        }

        // export function/class/const/let/interface/type/enum
        Stmt? decl = null;
        if (Match(TokenType.FUNCTION))
        {
            decl = FunctionDeclaration("function");
        }
        else if (Match(TokenType.CLASS))
        {
            decl = ClassDeclaration(isAbstract: false);
        }
        else if (Match(TokenType.ABSTRACT))
        {
            Consume(TokenType.CLASS, "Expect 'class' after 'abstract'.");
            decl = ClassDeclaration(isAbstract: true);
        }
        else if (Match(TokenType.CONST))
        {
            if (Match(TokenType.ENUM))
            {
                decl = EnumDeclaration(isConst: true);
            }
            else
            {
                decl = VarDeclaration();
            }
        }
        else if (Match(TokenType.LET))
        {
            decl = VarDeclaration();
        }
        else if (Match(TokenType.INTERFACE))
        {
            decl = InterfaceDeclaration();
        }
        else if (Match(TokenType.TYPE))
        {
            decl = TypeAliasDeclaration();
        }
        else if (Match(TokenType.ENUM))
        {
            decl = EnumDeclaration(isConst: false);
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
                Token localName = Consume(TokenType.IDENTIFIER, "Expect export name.");
                Token? exportedName = null;

                if (Match(TokenType.AS))
                {
                    exportedName = Consume(TokenType.IDENTIFIER, "Expect exported name after 'as'.");
                }

                specifiers.Add(new Stmt.ExportSpecifier(localName, exportedName));
            } while (Match(TokenType.COMMA));
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after export specifiers.");
        return specifiers;
    }
}
