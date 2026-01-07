using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Modules;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Module compilation methods for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Defines a module type with export fields.
    /// </summary>
    private void DefineModuleType(ParsedModule module)
    {
        // Create module class: $Module_<name>
        string moduleTypeName = $"$Module_{CompilationContext.SanitizeModuleName(module.ModuleName)}";
        var moduleType = _moduleBuilder.DefineType(
            moduleTypeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract
        );

        _moduleTypes[module.Path] = moduleType;
        var exportFields = new Dictionary<string, FieldBuilder>();

        // Create export fields
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Export export)
            {
                if (export.IsDefaultExport)
                {
                    // Default export field
                    var field = moduleType.DefineField(
                        "$default",
                        typeof(object),
                        FieldAttributes.Public | FieldAttributes.Static
                    );
                    exportFields["$default"] = field;
                }
                else if (export.Declaration != null)
                {
                    // Named export from declaration
                    string? exportName = GetExportDeclarationName(export.Declaration);
                    if (exportName != null)
                    {
                        var field = moduleType.DefineField(
                            exportName,
                            typeof(object),
                            FieldAttributes.Public | FieldAttributes.Static
                        );
                        exportFields[exportName] = field;
                    }
                }
                else if (export.NamedExports != null && export.FromModulePath == null)
                {
                    // Named exports like export { x, y as z }
                    foreach (var spec in export.NamedExports)
                    {
                        string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                        if (!exportFields.ContainsKey(exportedName))
                        {
                            var field = moduleType.DefineField(
                                exportedName,
                                typeof(object),
                                FieldAttributes.Public | FieldAttributes.Static
                            );
                            exportFields[exportedName] = field;
                        }
                    }
                }
                else if (export.FromModulePath != null && _moduleResolver != null)
                {
                    // Re-export: export { x } from './module' or export * from './module'
                    string sourcePath = _moduleResolver.ResolveModulePath(export.FromModulePath, module.Path);

                    if (export.NamedExports != null)
                    {
                        // export { x, y as z } from './module'
                        foreach (var spec in export.NamedExports)
                        {
                            string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                            if (!exportFields.ContainsKey(exportedName))
                            {
                                var field = moduleType.DefineField(
                                    exportedName,
                                    typeof(object),
                                    FieldAttributes.Public | FieldAttributes.Static
                                );
                                exportFields[exportedName] = field;
                            }
                        }
                    }
                    else
                    {
                        // export * from './module' - need source module's exports
                        // Source module is processed first (topological order)
                        if (_moduleExportFields.TryGetValue(sourcePath, out var sourceFields))
                        {
                            foreach (var (name, _) in sourceFields)
                            {
                                if (name == "$default") continue;  // * doesn't include default
                                if (!exportFields.ContainsKey(name))
                                {
                                    var field = moduleType.DefineField(
                                        name,
                                        typeof(object),
                                        FieldAttributes.Public | FieldAttributes.Static
                                    );
                                    exportFields[name] = field;
                                }
                            }
                        }
                    }
                }
            }
        }

        _moduleExportFields[module.Path] = exportFields;
    }

    /// <summary>
    /// Gets the name of an exported declaration.
    /// </summary>
    private string? GetExportDeclarationName(Stmt decl) => decl switch
    {
        Stmt.Function f => f.Name.Lexeme,
        Stmt.Class c => c.Name.Lexeme,
        Stmt.Var v => v.Name.Lexeme,
        Stmt.Enum e => e.Name.Lexeme,
        Stmt.Interface or Stmt.TypeAlias => null, // Type-only, no runtime export
        _ => null
    };

    /// <summary>
    /// Emits the initialization method for a module.
    /// </summary>
    private void EmitModuleInit(ParsedModule module)
    {
        var moduleType = _moduleTypes[module.Path];
        var exportFields = _moduleExportFields[module.Path];

        // Create $Initialize method
        var initMethod = moduleType.DefineMethod(
            "$Initialize",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );
        _moduleInitMethods[module.Path] = initMethod;

        var il = initMethod.GetILGenerator();
        var ctx = CreateCompilationContext(il);
        ctx.CurrentModulePath = module.Path;
        ctx.ModuleExportFields = _moduleExportFields;
        ctx.ModuleTypes = _moduleTypes;
        ctx.ModuleResolver = _moduleResolver;

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in module.Statements)
        {
            // Skip class, function, interface, type alias, and enum declarations
            // (they are compiled separately in earlier phases)
            if (stmt is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.TypeAlias or Stmt.Enum)
            {
                continue;
            }

            emitter.EmitStatement(stmt);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the entry point that initializes all modules in dependency order.
    /// </summary>
    private void EmitModulesEntryPoint(List<ParsedModule> modules)
    {
        var mainMethod = _programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );
        _entryPoint = mainMethod;

        var il = mainMethod.GetILGenerator();

        // Call each module's $Initialize method in dependency order
        foreach (var module in modules)
        {
            var initMethod = _moduleInitMethods[module.Path];
            il.Emit(OpCodes.Call, initMethod);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Creates a CompilationContext with common settings.
    /// </summary>
    private CompilationContext CreateCompilationContext(ILGenerator il)
    {
        return new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null,
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule
        };
    }
}
