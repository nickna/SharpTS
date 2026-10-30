using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Modules;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private sealed record ModuleSourceBinding(string Name, IReadOnlyList<Stmt> Declarations, FieldBuilder Field, FieldBuilder? Initialized);
    private readonly Dictionary<string, Dictionary<string, ModuleSourceBinding>> _liveModuleBindings = [];
    private readonly HashSet<Stmt> _liveModuleDeclarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, HashSet<string>> _liveModuleCaptureNames = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(string Module, string Name), (string Module, string Name)> _liveModuleExportAliases = [];
    private readonly Dictionary<(string Module, string Name), (string Module, string Name)> _liveModuleImportAliases = [];

    private void PrepareLiveModuleBindings(List<ParsedModule> modules)
    {
        var declarations = new Dictionary<string, Dictionary<string, List<Stmt>>>();
        foreach (var module in modules.Where(IsLiveBindingModule))
        {
            var names = new Dictionary<string, List<Stmt>>(StringComparer.Ordinal);
            foreach (var statement in module.Statements)
                foreach (var declaration in SourceDeclarations(statement))
                    if (GetExportDeclarationName(declaration) is { } name)
                    {
                        if (!names.TryGetValue(name, out var items)) names[name] = items = [];
                        items.Add(declaration);
                    }
            declarations[module.Path] = names;
            _liveModuleBindings[module.Path] = new(StringComparer.Ordinal);
        }
        var ownedExports = new HashSet<(string Module, string Name)>();
        foreach (var module in modules.Where(IsLiveBindingModule))
            foreach (var statement in module.Statements)
                if (statement is Stmt.Import { IsTypeOnly: false } import &&
                    TryLiveSource(import.ModulePath, module.Path, out var importedSource))
                {
                    if (import.DefaultImport is { } defaultImport)
                        _liveModuleImportAliases[(module.Path, defaultImport.Lexeme)] = (importedSource, "default");
                    foreach (var spec in import.NamedImports ?? [])
                        if (!spec.IsTypeOnly)
                            _liveModuleImportAliases[(module.Path, spec.LocalName?.Lexeme ?? spec.Imported.Lexeme)] = (importedSource, spec.Imported.Lexeme);
                }
        foreach (var module in modules.Where(IsLiveBindingModule))
        {
            foreach (var statement in module.Statements)
            {
                if (statement is not Stmt.Export { IsTypeOnly: false, ExportAssignment: null } export) continue;
                if (export.Declaration is { } exportedDeclaration)
                {
                    foreach (var declaration in ExportDeclarationSequence.Declarations(exportedDeclaration))
                        if (GetExportDeclarationName(declaration) is { } local)
                            Own(module.Path, local, export.IsDefaultExport ? "default" : local);
                }
                else if (export.FromModulePath is { } from && TryLiveSource(from, module.Path, out var reexportSource))
                {
                    if (export.NamedExports is { } named)
                    {
                        foreach (var spec in named)
                            if (!spec.IsTypeOnly)
                                _liveModuleExportAliases[(module.Path, spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme)] = (reexportSource, spec.LocalName.Lexeme);
                    }
                    else if (_modules.ExportFields.TryGetValue(reexportSource, out var fields))
                        foreach (var name in fields.Keys)
                            if (name != "default" && !ownedExports.Contains((module.Path, name)))
                                _liveModuleExportAliases.TryAdd((module.Path, name), (reexportSource, name));
                }
                else if (export.NamedExports is { } localExports)
                    foreach (var spec in localExports)
                        if (!spec.IsTypeOnly) Own(module.Path, spec.LocalName.Lexeme, spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme);
            }
        }
        var visiting = new HashSet<(string Module, string Name)>();
        var resolved = new Dictionary<(string Module, string Name), FieldBuilder>();
        foreach (var slot in _liveModuleExportAliases.Keys)
            if (Resolve(slot) is { } field) _modules.ExportFields[slot.Module][slot.Name] = field;
        foreach (var (slot, source) in _liveModuleImportAliases)
        {
            if (Resolve(source) is not { } field || !_modules.ImportFields[slot.Module].ContainsKey(slot.Name)) continue;
            _modules.ImportFields[slot.Module][slot.Name] = field;
            if (InitializationField(field) is { } initialized)
            {
                if (!_closures.ModuleTopLevelLexicalInitFields.TryGetValue(slot.Module, out var flags))
                    _closures.ModuleTopLevelLexicalInitFields[slot.Module] = flags = [];
                flags[slot.Name] = initialized;
            }
        }

        void Own(string path, string local, string exported)
        {
            if (!_modules.ExportFields[path].TryGetValue(exported, out var field)) return;
            if (declarations[path].TryGetValue(local, out var sourceDeclarations))
            {
                ownedExports.Add((path, exported));
                _liveModuleExportAliases.Remove((path, exported));
                if (!_liveModuleBindings[path].TryGetValue(local, out var binding))
                {
                    FieldBuilder? initialized = null;
                    bool variable = sourceDeclarations.Any(item => item is Stmt.Var or Stmt.Const);
                    bool function = sourceDeclarations.Any(item => item is Stmt.Function { Body: not null, IsDeclare: false });
                    if (sourceDeclarations.Any(item => item is Stmt.Const or Stmt.Var { IsVar: false }))
                    {
                        initialized = _programType.DefineField($"$liveExport_{GetDefinitionContext().GetSanitizedModuleName(path)}_{local}$initialized", _types.Boolean, FieldAttributes.Public | FieldAttributes.Static);
                        if (!_closures.ModuleTopLevelLexicalInitFields.TryGetValue(path, out var flags))
                            _closures.ModuleTopLevelLexicalInitFields[path] = flags = [];
                        flags[local] = initialized;
                    }
                    binding = new(local, sourceDeclarations.AsReadOnly(), field, initialized);
                    _liveModuleBindings[path][local] = binding;
                    if (variable || function)
                    {
                        if (!_moduleTopLevelStaticVars.TryGetValue(path, out var variables))
                            _moduleTopLevelStaticVars[path] = variables = [];
                        variables[local] = field;
                        foreach (var declaration in sourceDeclarations.Where(item => item is Stmt.Var or Stmt.Const))
                            _liveModuleDeclarations.Add(declaration);
                    }
                }
                _modules.ExportFields[path][exported] = binding.Field;
            }
            else if (_liveModuleImportAliases.TryGetValue((path, local), out var source))
                _liveModuleExportAliases[(path, exported)] = source;
        }
        FieldBuilder? Resolve((string Module, string Name) slot)
        {
            if (resolved.TryGetValue(slot, out var field)) return field;
            if (!visiting.Add(slot)) return null;
            try
            {
                field = _liveModuleExportAliases.TryGetValue(slot, out var source) ? Resolve(source) :
                    _modules.ExportFields.TryGetValue(slot.Module, out var fields) ? fields.GetValueOrDefault(slot.Name) : null;
                if (field is not null) resolved[slot] = field;
                return field;
            }
            finally { visiting.Remove(slot); }
        }
        FieldBuilder? InitializationField(FieldBuilder field) => _liveModuleBindings.Values
            .SelectMany(bindings => bindings.Values).FirstOrDefault(binding => ReferenceEquals(binding.Field, field))?.Initialized;
        bool TryLiveSource(string specifier, string path, out string source)
        {
            source = "";
            if (Runtime.BuiltIns.Modules.BuiltInModuleRegistry.GetModuleName(specifier) != null || _modules.Resolver is null) return false;
            source = _modules.Resolver.ResolveRuntimeModulePath(specifier, path);
            return declarations.ContainsKey(source);
        }
    }

    private void EmitLiveModuleFunctionInitialization(ParsedModule module, ILEmitter emitter)
    {
        if (!_liveModuleBindings.TryGetValue(module.Path, out var bindings)) return;
        foreach (var binding in bindings.Values)
            if (binding.Declarations.OfType<Stmt.Function>().FirstOrDefault(function => function.Body is not null && !function.IsDeclare) is { } declaration)
                emitter.EmitHoistedModuleFunction(declaration, binding.Field);
    }

    private void RemoveLiveModuleCaptures(object callable, HashSet<string> captures, string? path)
    {
        if (path is null || !_liveModuleBindings.TryGetValue(path, out var bindings) ||
            !_moduleTopLevelStaticVars.TryGetValue(path, out var fields)) return;
        foreach (var name in captures.ToArray())
        {
            if (!bindings.TryGetValue(name, out var binding) || !fields.TryGetValue(name, out var field) ||
                !ReferenceEquals(binding.Field, field) || _closures.Analyzer.GetCaptureSource(callable, name) is not null ||
                _closures.Analyzer.IsDirectTopLevelBlockScopedCapture(callable, name) ||
                callable is Expr.ArrowFunction { Name: { } self } && self.Lexeme == name)
                continue;
            captures.Remove(name);
            if (!_liveModuleCaptureNames.TryGetValue(callable, out var removed))
                _liveModuleCaptureNames[callable] = removed = [];
            removed.Add(name);
        }
    }

    private IEnumerable<string> OriginalModuleCaptures(object callable, HashSet<string> captures) =>
        _liveModuleCaptureNames.TryGetValue(callable, out var removed) ? captures.Concat(removed) : captures;

    private static bool IsLiveBindingModule(ParsedModule module) => !module.IsScript && !module.IsCommonJs &&
        !module.IsDotNetModule && !module.IsDotNetExtensionModule &&
        !module.Statements.Any(statement => statement is Stmt.Export { ExportAssignment: not null });

    private static IEnumerable<Stmt> SourceDeclarations(Stmt statement)
    {
        if (statement is Stmt.Export { Declaration: { } declaration })
            return ExportDeclarationSequence.Declarations(declaration);
        return ExportDeclarationSequence.Declarations(statement);
    }
}
