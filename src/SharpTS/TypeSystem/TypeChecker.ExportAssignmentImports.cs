using SharpTS.Modules;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private bool ExportAssignmentRequiresDefaultImport(ParsedModule module)
    {
        var kind = new ExportAssignmentDeclarationResolver(_moduleResolver!).ResolveModule(module, []);
        return kind.HasFlag(ExportAssignmentDeclarationKind.Other) &&
            (kind & (ExportAssignmentDeclarationKind.Variable | ExportAssignmentDeclarationKind.Module)) == 0;
    }

    [Flags]
    private enum ExportAssignmentDeclarationKind { Unknown = 0, Variable = 1, Module = 2, Other = 4 }

    // TS2497 depends on the assigned symbol's declarations, not its callable/constructable
    // type. In particular, a callable variable is allowed while a bare function is not.
    private sealed class ExportAssignmentDeclarationResolver(ModuleResolver resolver)
    {
        private sealed record Scope(ParsedModule Module, IReadOnlyList<Stmt> Statements, Scope? Parent);
        private readonly Dictionary<ParsedModule, Scope> _modules = [];
        private readonly Dictionary<(Scope Scope, string Name), Scope> _namespaces = [];
        private readonly HashSet<(Scope Scope, string Path, bool Exported)> _active = [];

        public ExportAssignmentDeclarationKind ResolveModule(ParsedModule module, string[] path)
        {
            if (!_modules.TryGetValue(module, out var scope))
                _modules[module] = scope = new Scope(module, module.Statements, null);
            var assignment = module.Statements.OfType<Stmt.Export>()
                .FirstOrDefault(export => export.ExportAssignment is not null)?.ExportAssignment;
            if (assignment is not null)
                return ExpressionPath(assignment) is { } target
                    ? ResolveLocal(scope, [.. target, .. path]) : ExportAssignmentDeclarationKind.Unknown;
            return path.Length == 0 ? ExportAssignmentDeclarationKind.Module : ResolveExport(scope, path);
        }

        private ExportAssignmentDeclarationKind ResolveLocal(Scope scope, string[] path, bool searchParents = true)
        {
            if (path.Length == 0 || !_active.Add((scope, string.Join('\0', path), false)))
                return ExportAssignmentDeclarationKind.Unknown;
            try
            {
                string name = path[0];
                string[] remainder = path[1..];
                var declarations = Declarations(scope.Statements).ToList();
                ExportAssignmentDeclarationKind result = 0;
                bool found = false;
                foreach (var declaration in declarations)
                {
                    if (declaration is Stmt.Import import)
                    {
                        if (import.NamespaceImport?.Lexeme == name || import.DefaultImport?.Lexeme == name ||
                            import.NamedImports?.Any(spec => (spec.LocalName ?? spec.Imported).Lexeme == name) == true)
                        {
                            found = true;
                            result |= ResolveImport(scope, import, name, remainder);
                        }
                        continue;
                    }
                    if (DeclarationName(declaration) != name) continue;
                    found = true;
                    result |= declaration switch
                    {
                        Stmt.Var or Stmt.Const when remainder.Length == 0 => ExportAssignmentDeclarationKind.Variable,
                        Stmt.Namespace when remainder.Length == 0 => ExportAssignmentDeclarationKind.Module,
                        Stmt.Namespace => ResolveLocal(NamespaceScope(scope, name, declarations), remainder, false),
                        Stmt.Function or Stmt.Class or Stmt.Enum when remainder.Length == 0 => ExportAssignmentDeclarationKind.Other,
                        Stmt.ImportAlias alias => ResolveLocal(scope,
                            [.. alias.QualifiedPath.Select(token => token.Lexeme), .. remainder]),
                        Stmt.ImportRequire require => Dependency(scope, require.ModulePath) is { } dependency
                            ? ResolveModule(dependency, remainder) : ExportAssignmentDeclarationKind.Unknown,
                        _ => ExportAssignmentDeclarationKind.Unknown
                    };
                }
                return !found && searchParents && scope.Parent is { } parent
                    ? ResolveLocal(parent, path) : result;
            }
            finally { _active.Remove((scope, string.Join('\0', path), false)); }
        }

        private ExportAssignmentDeclarationKind ResolveExport(Scope scope, string[] path)
        {
            if (path.Length == 0 || !_active.Add((scope, string.Join('\0', path), true)))
                return ExportAssignmentDeclarationKind.Unknown;
            try
            {
                ExportAssignmentDeclarationKind result = scope.Module.IsAmbientModule
                    ? ResolveLocal(scope, path, false) : ExportAssignmentDeclarationKind.Unknown;
                foreach (var statement in scope.Statements)
                {
                    if (statement is Stmt.Export export)
                    {
                        if (export.IsTypeOnly) continue;
                        if (export.IsDefaultExport && path[0] == "default")
                        {
                            if (export.Declaration is { } declaration)
                            {
                                result |= DeclarationName(declaration) is { } name
                                    ? ResolveLocal(scope, [name, .. path[1..]])
                                    : ExportAssignmentDeclarationKind.Other;
                            }
                            else if (export.DefaultExpr is { } expression && ExpressionPath(expression) is { } target)
                                result |= ResolveLocal(scope, [.. target, .. path[1..]]);
                        }
                        else if (export.Declaration is { } declaration && DeclarationName(declaration) == path[0])
                            result |= ResolveLocal(scope, path);
                        foreach (var spec in export.NamedExports ?? [])
                            if (!spec.IsTypeOnly && (spec.ExportedName ?? spec.LocalName).Lexeme == path[0])
                            {
                                string[] target = [spec.LocalName.Lexeme, .. path[1..]];
                                result |= export.FromModulePath is { } from && Dependency(scope, from) is { } dependency
                                    ? ResolveModule(dependency, target) : ResolveLocal(scope, target);
                            }
                        if (export.FromModulePath is { } modulePath && export.NamedExports is null &&
                            Dependency(scope, modulePath) is { } starDependency)
                        {
                            if (export.NamespaceExportName is { } namespaceName)
                            {
                                if (namespaceName.Lexeme == path[0]) result |= ResolveModule(starDependency, path[1..]);
                            }
                            else result |= ResolveModule(starDependency, path);
                        }
                    }
                    else if (statement is Stmt.ImportAlias { IsExported: true } alias && alias.AliasName.Lexeme == path[0] ||
                             statement is Stmt.ImportRequire { IsExported: true } require && require.AliasName.Lexeme == path[0])
                        result |= ResolveLocal(scope, path);
                }
                return result;
            }
            finally { _active.Remove((scope, string.Join('\0', path), true)); }
        }

        private ExportAssignmentDeclarationKind ResolveImport(Scope scope, Stmt.Import import, string name, string[] remainder)
        {
            if (Dependency(scope, import.ModulePath) is not { } module) return ExportAssignmentDeclarationKind.Unknown;
            if (import.NamespaceImport?.Lexeme == name) return ResolveModule(module, remainder);
            if (import.DefaultImport?.Lexeme == name)
                return module.Statements.OfType<Stmt.Export>().Any(export => export.ExportAssignment is not null)
                    ? ResolveModule(module, remainder) : ResolveModule(module, ["default", .. remainder]);
            var spec = import.NamedImports?.FirstOrDefault(spec => (spec.LocalName ?? spec.Imported).Lexeme == name);
            return spec is null ? ExportAssignmentDeclarationKind.Unknown : ResolveModule(module, [spec.Imported.Lexeme, .. remainder]);
        }

        private ParsedModule? Dependency(Scope scope, string specifier)
        {
            try { return resolver.GetCachedModule(resolver.ResolveModulePath(specifier, scope.Module.Path)); }
            catch (ModuleResolutionException) { return null; }
        }

        private Scope NamespaceScope(Scope parent, string name, List<Stmt> declarations)
        {
            if (!_namespaces.TryGetValue((parent, name), out var scope))
                _namespaces[(parent, name)] = scope = new Scope(parent.Module,
                    declarations.OfType<Stmt.Namespace>().Where(ns => ns.Name.Lexeme == name)
                        .SelectMany(ns => ns.Members).ToList(), parent);
            return scope;
        }

        private static IEnumerable<Stmt> Declarations(IEnumerable<Stmt> statements)
        {
            foreach (var statement in statements)
            {
                if (statement is Stmt.Export { Declaration: { } declaration })
                    yield return declaration;
                else if (statement is Stmt.Sequence sequence)
                    foreach (var item in Declarations(sequence.Statements)) yield return item;
                else yield return statement;
            }
        }

        private static string? DeclarationName(Stmt declaration) => declaration switch
        {
            Stmt.Var variable => variable.Name.Lexeme,
            Stmt.Const constant => constant.Name.Lexeme,
            Stmt.Function function => function.Name.Lexeme,
            Stmt.Class @class => @class.Name.Lexeme,
            Stmt.Enum @enum => @enum.Name.Lexeme,
            Stmt.Namespace ns => ns.Name.Lexeme,
            Stmt.ImportAlias alias => alias.AliasName.Lexeme,
            Stmt.ImportRequire require => require.AliasName.Lexeme,
            _ => null
        };

        private static string[]? ExpressionPath(Expr expression) => expression switch
        {
            Expr.Variable variable => [variable.Name.Lexeme],
            Expr.Get get when ExpressionPath(get.Object) is { } parent => [.. parent, get.Name.Lexeme],
            Expr.Grouping grouping => ExpressionPath(grouping.Expression),
            _ => null
        };
    }
}
