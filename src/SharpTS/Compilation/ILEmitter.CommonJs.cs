using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// CommonJS-specific lowering hooks that live in <see cref="ILEmitter"/> rather than the
/// shared base. The shared CJS helpers (require/module.exports/exports patterns) are on
/// <see cref="ExpressionEmitterBase"/> so all four state machine emitters inherit them.
/// </summary>
/// <remarks>
/// Only the import-statement lowering needs to live here because it pokes at private ILEmitter
/// state (<see cref="ILEmitter._ctx"/> Locals helpers) and is never invoked from a state machine
/// context (imports only appear at the top of an ESM module body).
/// </remarks>
public partial class ILEmitter
{
    /// <summary>
    /// Emits an ESM <c>import</c> statement whose target is a CommonJS module.
    /// Default and namespace imports load the entire <c>$exports</c> value.
    /// Named imports load <c>$exports</c> and pull each name off via dynamic property access.
    /// </summary>
    internal void EmitImportFromCommonJs(Stmt.Import import, string importedPath, MethodBuilder cjsGetExports)
    {
        Dictionary<string, FieldBuilder>? currentModuleImportFields = null;
        if (_ctx.CurrentModulePath != null)
        {
            _ctx.ModuleImportFields?.TryGetValue(_ctx.CurrentModulePath, out currentModuleImportFields);
        }

        void StoreInBinding(string localName)
        {
            if (currentModuleImportFields?.TryGetValue(localName, out var importField) == true)
            {
                IL.Emit(OpCodes.Stsfld, importField);
            }
            else
            {
                var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, local);
            }
        }

        // Default import: import x from './foo.cjs' → x = $Module_foo.$GetExports()
        if (import.DefaultImport != null)
        {
            IL.Emit(OpCodes.Call, cjsGetExports);
            StoreInBinding(import.DefaultImport.Lexeme);
        }

        // Namespace import: import * as x from './foo.cjs' → same as default for CJS.
        if (import.NamespaceImport != null)
        {
            IL.Emit(OpCodes.Call, cjsGetExports);
            StoreInBinding(import.NamespaceImport.Lexeme);
        }

        // Named imports: import { a, b } from './foo.cjs' → a = exports.a, b = exports.b
        if (import.NamedImports != null)
        {
            foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
            {
                string importedName = spec.Imported.Lexeme;
                string localName = spec.LocalName?.Lexeme ?? importedName;

                IL.Emit(OpCodes.Call, cjsGetExports);
                IL.Emit(OpCodes.Ldstr, importedName);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
                StoreInBinding(localName);
            }
        }
    }
}
