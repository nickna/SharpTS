using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Module statement type checking - handles export statements and module-related checking.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Type-checks an export statement and registers exports in the current module.
    /// </summary>
    private void CheckExportStatement(Stmt.Export exportStmt)
    {
        // Handle export assignment: export = expr
        if (exportStmt.ExportAssignment != null)
        {
            if (_currentModule != null)
            {
                // Already handled in CollectModuleExports - skip if type is already set
                if (_currentModule.ExportAssignmentType != null)
                {
                    return;
                }

                // Validate mutual exclusion: cannot use export = with other exports
                if (_currentModule.ExportedTypes.Count > 0 || _currentModule.DefaultExportType != null)
                {
                    throw new TypeCheckException(
                        "An export assignment cannot be used in a module with other exported elements.",
                        exportStmt.Keyword.Line);
                }
                if (_currentModule.HasExportAssignment)
                {
                    throw new TypeCheckException(
                        "A module cannot have multiple 'export =' declarations.",
                        exportStmt.Keyword.Line);
                }

                _currentModule.HasExportAssignment = true;
                _currentModule.ExportAssignmentType = CheckExpr(exportStmt.ExportAssignment);
            }
            else
            {
                // Not in module context - still type-check the expression
                CheckExpr(exportStmt.ExportAssignment);
            }
            return;
        }

        // Block other exports if export = was used
        if (_currentModule?.HasExportAssignment == true)
        {
            throw new TypeCheckException(
                "An export assignment cannot be used in a module with other exported elements.",
                exportStmt.Keyword.Line);
        }

        if (exportStmt.IsDefaultExport)
        {
            // export default expression or export default class/function
            if (exportStmt.Declaration != null)
            {
                CheckStmt(exportStmt.Declaration);
                if (_currentModule != null)
                {
                    _currentModule.DefaultExportType = GetDeclaredType(exportStmt.Declaration);
                }
            }
            else if (exportStmt.DefaultExpr != null)
            {
                var type = CheckExpr(exportStmt.DefaultExpr);
                if (_currentModule != null)
                {
                    _currentModule.DefaultExportType = type;
                }
            }
        }
        else if (exportStmt.Declaration != null)
        {
            // export class/function/const/let
            CheckStmt(exportStmt.Declaration);
            if (_currentModule != null)
            {
                string name = GetDeclarationName(exportStmt.Declaration);
                var type = GetDeclaredType(exportStmt.Declaration);
                _currentModule.ExportedTypes[name] = type;
            }
        }
        else if (exportStmt.NamedExports != null && exportStmt.FromModulePath == null)
        {
            // export { x, y } - verify each exported name exists
            foreach (var spec in exportStmt.NamedExports)
            {
                var type = LookupVariable(spec.LocalName);
                if (_currentModule != null)
                {
                    string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                    _currentModule.ExportedTypes[exportedName] = type;
                }
            }
        }
        else if (exportStmt.FromModulePath != null)
        {
            // Re-export: export { x } from './module' or export * from './module'
            // The actual binding happens during module resolution
            // Here we just need to validate the syntax is correct
        }
    }

    /// <summary>
    /// Type-checks a CommonJS-style require import: import x = require('path')
    /// </summary>
    private void CheckImportRequire(Stmt.ImportRequire importReq)
    {
        // Check if it's a built-in module (fs, path, os, etc.)
        string? builtInModuleName = BuiltInModuleRegistry.GetModuleName(importReq.ModulePath);
        if (builtInModuleName != null)
        {
            // Built-in module - define as any type (or we could define specific types)
            _environment.Define(importReq.AliasName.Lexeme, new TypeInfo.Any());
            return;
        }

        // Not in module context - allow but define as any
        if (_currentModule == null || _moduleResolver == null)
        {
            _environment.Define(importReq.AliasName.Lexeme, new TypeInfo.Any());
            return;
        }

        // Resolve the module path
        string resolvedPath = _moduleResolver.ResolveModulePath(importReq.ModulePath, _currentModule.Path);

        // Try to find the imported module via the resolver cache
        var importedModule = _moduleResolver.GetCachedModule(resolvedPath);
        if (importedModule == null)
        {
            // Module not found - allow but define as any (might be external)
            _environment.Define(importReq.AliasName.Lexeme, new TypeInfo.Any());
            return;
        }

        TypeInfo importedType;
        if (importedModule.HasExportAssignment && importedModule.ExportAssignmentType != null)
        {
            // Module uses export = value - import the assignment value directly
            importedType = importedModule.ExportAssignmentType;
        }
        else
        {
            // ES6-style module - create a namespace type with all exports
            var exports = importedModule.ExportedTypes.ToFrozenDictionary();
            importedType = new TypeInfo.Record(exports);
        }

        _environment.Define(importReq.AliasName.Lexeme, importedType);

        // If this is a re-export (export import x = require('...')), register the export
        if (importReq.IsExported && _currentModule != null)
        {
            _currentModule.ExportedTypes[importReq.AliasName.Lexeme] = importedType;
        }
    }
}
