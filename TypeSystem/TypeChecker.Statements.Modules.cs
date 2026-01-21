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
    /// Type-checks a declare module statement (module augmentation or ambient declaration).
    /// The actual merging logic is applied during CheckModules phase.
    /// </summary>
    private void CheckDeclareModuleStatement(Stmt.DeclareModule declareModule)
    {
        // Store in the current module for processing during module type checking
        if (_currentModule != null)
        {
            // Check if this is augmenting an existing module or declaring an ambient module
            bool isAugmentation = false;

            // Try to resolve the module path to determine if it's augmentation vs ambient
            if (_moduleResolver != null)
            {
                try
                {
                    string resolvedPath = _moduleResolver.ResolveModulePath(declareModule.ModulePath, _currentModule.Path);
                    var targetModule = _moduleResolver.GetCachedModule(resolvedPath);
                    isAugmentation = targetModule != null;
                }
                catch
                {
                    // Path couldn't be resolved - treat as ambient declaration
                    isAugmentation = false;
                }
            }

            if (isAugmentation)
            {
                // Store as module augmentation
                if (!_currentModule.ModuleAugmentations.TryGetValue(declareModule.ModulePath, out var members))
                {
                    members = [];
                    _currentModule.ModuleAugmentations[declareModule.ModulePath] = members;
                }
                members.AddRange(declareModule.Members);
            }
            else
            {
                // Store as ambient module declaration
                if (!_currentModule.AmbientModules.TryGetValue(declareModule.ModulePath, out var members))
                {
                    members = [];
                    _currentModule.AmbientModules[declareModule.ModulePath] = members;
                }
                members.AddRange(declareModule.Members);
            }
        }

        // Type-check the member declarations to validate their types
        foreach (var member in declareModule.Members)
        {
            CheckDeclareBlockMember(member);
        }
    }

    /// <summary>
    /// Type-checks a declare global statement (global augmentation).
    /// Merges declarations into the global scope.
    /// </summary>
    private void CheckDeclareGlobalStatement(Stmt.DeclareGlobal declareGlobal)
    {
        // Store in the current module for processing during module type checking
        if (_currentModule != null)
        {
            _currentModule.GlobalAugmentations.AddRange(declareGlobal.Members);
        }

        // Type-check and merge each declaration into the global environment
        foreach (var member in declareGlobal.Members)
        {
            CheckAndMergeGlobalMember(member);
        }
    }

    /// <summary>
    /// Type-checks a member inside a declare module or declare global block.
    /// These are type-only declarations.
    /// </summary>
    private void CheckDeclareBlockMember(Stmt member)
    {
        switch (member)
        {
            case Stmt.Export export when export.Declaration != null:
                CheckDeclareBlockMember(export.Declaration);
                break;

            case Stmt.Interface interfaceStmt:
                CheckInterfaceDeclaration(interfaceStmt);
                break;

            case Stmt.Function funcStmt:
                // Ambient functions have no body - just register the signature
                CheckFunctionDeclaration(funcStmt);
                break;

            case Stmt.Var varStmt:
                // Ambient variable - register with declared type
                TypeInfo varType = varStmt.TypeAnnotation != null
                    ? ToTypeInfo(varStmt.TypeAnnotation)
                    : new TypeInfo.Any();
                _environment.Define(varStmt.Name.Lexeme, varType);
                break;

            case Stmt.Class classStmt:
                CheckClassDeclaration(classStmt);
                break;

            case Stmt.TypeAlias typeAlias:
                if (typeAlias.TypeParameters != null && typeAlias.TypeParameters.Count > 0)
                {
                    var typeParamNames = typeAlias.TypeParameters.Select(tp => tp.Name.Lexeme).ToList();
                    _environment.DefineGenericTypeAlias(typeAlias.Name.Lexeme, typeAlias.TypeDefinition, typeParamNames);
                }
                else
                {
                    _environment.DefineTypeAlias(typeAlias.Name.Lexeme, typeAlias.TypeDefinition);
                }
                break;

            case Stmt.Namespace ns:
                CheckNamespace(ns);
                break;

            default:
                // Other statements are not valid in declare blocks
                break;
        }
    }

    /// <summary>
    /// Type-checks and merges a member from declare global into the global environment.
    /// Supports interface merging for built-in types like Array, String, etc.
    /// </summary>
    private void CheckAndMergeGlobalMember(Stmt member)
    {
        switch (member)
        {
            case Stmt.Export export when export.Declaration != null:
                CheckAndMergeGlobalMember(export.Declaration);
                break;

            case Stmt.Interface interfaceStmt:
                // Check if this interface merges with an existing global type
                MergeGlobalInterface(interfaceStmt);
                break;

            case Stmt.Function funcStmt:
                CheckFunctionDeclaration(funcStmt);
                break;

            case Stmt.Var varStmt:
                TypeInfo varType = varStmt.TypeAnnotation != null
                    ? ToTypeInfo(varStmt.TypeAnnotation)
                    : new TypeInfo.Any();
                _environment.Define(varStmt.Name.Lexeme, varType);
                break;

            default:
                CheckDeclareBlockMember(member);
                break;
        }
    }

    /// <summary>
    /// Merges an interface declaration from declare global into the global scope.
    /// If the interface already exists (e.g., Array, String), merges the members.
    /// </summary>
    private void MergeGlobalInterface(Stmt.Interface interfaceStmt)
    {
        string name = interfaceStmt.Name.Lexeme;

        // Check if there's an existing type with this name
        TypeInfo? existingType = _environment.Get(name);
        if (existingType != null)
        {
            if (existingType is TypeInfo.Interface existingInterface)
            {
                // Merge the new members into the existing interface
                var mergedMembers = new Dictionary<string, TypeInfo>(existingInterface.Members);
                var mergedOptional = new HashSet<string>(existingInterface.OptionalMembers);

                foreach (var member in interfaceStmt.Members)
                {
                    var memberType = ToTypeInfo(member.Type);
                    mergedMembers[member.Name.Lexeme] = memberType;
                    if (member.IsOptional)
                    {
                        mergedOptional.Add(member.Name.Lexeme);
                    }
                }

                // Create the merged interface
                var mergedInterface = existingInterface with
                {
                    Members = mergedMembers.ToFrozenDictionary(),
                    OptionalMembers = mergedOptional.ToFrozenSet()
                };

                // Re-define the merged interface (replaces existing definition)
                _environment.Define(name, mergedInterface);
            }
            else
            {
                // Can't merge interface with non-interface type
                throw new TypeCheckException(
                    $"Cannot augment '{name}': existing type is not an interface",
                    interfaceStmt.Name.Line);
            }
        }
        else
        {
            // No existing type - just define the new interface
            CheckInterfaceDeclaration(interfaceStmt);
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
