using SharpTS.Parsing;

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
}
