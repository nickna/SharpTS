using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private ModuleInstance CreateModuleInstance(ParsedModule module)
    {
        var instance = new ModuleInstance();
        if (module.IsScript || module.IsCommonJs || module.IsBuiltIn
            || module.IsDotNetModule || module.IsDotNetExtensionModule
            || module.Statements.Any(statement => statement is Stmt.Export { ExportAssignment: not null }))
            return instance;

        instance.HasLiveNamespaceBindings = true;
        // Establish explicit runtime export names before a self import can
        // observe the namespace. Values remain unread until requested.
        foreach (var statement in module.Statements)
        {
            if (statement is Stmt.ImportRequire { IsExported: true } importRequire)
                instance.DeclareExport(importRequire.AliasName.Lexeme);
            if (statement is not Stmt.Export { IsTypeOnly: false } export) continue;
            if (export.IsDefaultExport)
                instance.DeclareExport("default");
            else if (export.Declaration is { } declaration)
            {
                foreach (var item in ExportDeclarationSequence.Declarations(declaration))
                    if (!IsTypeOnlyDeclaration(item)) instance.DeclareExport(GetDeclaredName(item));
            }
            else if (export.NamedExports is { } named)
            {
                foreach (var spec in named)
                    if (!spec.IsTypeOnly)
                        instance.DeclareExport(spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme);
            }
        }
        return instance;
    }
}
