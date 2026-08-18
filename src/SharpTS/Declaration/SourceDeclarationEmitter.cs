using System.Globalization;
using System.Text;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Declaration;

/// <summary>A generated TypeScript declaration file and its source/output paths.</summary>
public sealed record DeclarationOutput(string SourcePath, string OutputPath, string Content);

/// <summary>
/// Emits declaration files from the checked source AST. This is intentionally separate from
/// <see cref="DiscoveryGenerator"/>, which describes CLR reflection metadata for interop.
/// </summary>
public static class SourceDeclarationEmitter
{
    public static IReadOnlyList<DeclarationOutput> EmitModules(
        IEnumerable<ParsedModule> modules,
        TypeMap typeMap,
        IEnumerable<string> sourcePaths,
        string? rootDir = null,
        string? declarationDir = null,
        string? outDir = null)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var moduleByPath = modules
            .Where(module => IsPhysicalSource(module.Path))
            .ToDictionary(module => Path.GetFullPath(module.Path), comparer);
        var requestedPaths = sourcePaths
            .Where(IsPhysicalSource)
            .Select(Path.GetFullPath)
            .Distinct(comparer)
            .ToArray();
        string? commonRoot = rootDir is not null
            ? Path.GetFullPath(rootDir)
            : declarationDir is not null || outDir is not null
                ? FindCommonDirectory(requestedPaths)
                : null;
        string? outputRoot = declarationDir ?? outDir;
        if (outputRoot is not null)
            outputRoot = Path.GetFullPath(outputRoot);

        var outputs = new List<DeclarationOutput>();
        var outputPaths = new HashSet<string>(comparer);
        foreach (string sourcePath in requestedPaths)
        {
            if (!moduleByPath.TryGetValue(sourcePath, out var module))
                continue;

            string outputPath = GetOutputPath(sourcePath, commonRoot, outputRoot);
            if (!outputPaths.Add(outputPath))
                throw new DeclarationEmitException(
                    $"Multiple source files map to declaration output '{outputPath}'.");

            outputs.Add(new DeclarationOutput(
                sourcePath,
                outputPath,
                EmitFile(module.Statements, module, typeMap)));
        }
        return outputs;
    }

    public static DeclarationOutput EmitSingleFile(
        string sourcePath,
        IReadOnlyList<Stmt> statements,
        TypeMap typeMap,
        string? rootDir = null,
        string? declarationDir = null,
        string? outDir = null)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        string? outputRoot = declarationDir ?? outDir;
        if (outputRoot is not null)
            outputRoot = Path.GetFullPath(outputRoot);
        string outputPath = GetOutputPath(
            sourcePath,
            rootDir is null ? Path.GetDirectoryName(sourcePath) : Path.GetFullPath(rootDir),
            outputRoot);
        return new DeclarationOutput(
            sourcePath,
            outputPath,
            EmitFile(statements, module: null, typeMap));
    }

    /// <summary>Writes a fully planned output set using replace-on-success temporary files.</summary>
    public static void WriteAll(IEnumerable<DeclarationOutput> outputs)
    {
        foreach (DeclarationOutput output in outputs)
        {
            if (File.Exists(output.OutputPath) &&
                string.Equals(File.ReadAllText(output.OutputPath), output.Content, StringComparison.Ordinal))
            {
                continue;
            }

            string? directory = Path.GetDirectoryName(output.OutputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = output.OutputPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, output.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temporaryPath, output.OutputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }

    private static string EmitFile(
        IReadOnlyList<Stmt> statements,
        ParsedModule? module,
        TypeMap typeMap)
    {
        var emitter = new FileEmitter(module, typeMap);
        return emitter.Emit(statements);
    }

    private static string GetOutputPath(string sourcePath, string? rootDir, string? outputRoot)
    {
        string fileName = GetDeclarationFileName(sourcePath);
        if (outputRoot is null)
            return Path.Combine(Path.GetDirectoryName(sourcePath)!, fileName);

        string relativeDirectory = "";
        if (rootDir is not null)
        {
            string relative = Path.GetRelativePath(rootDir, sourcePath);
            if (relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
            {
                throw new DeclarationEmitException(
                    $"Source file '{sourcePath}' is outside declaration rootDir '{rootDir}'.");
            }
            relativeDirectory = Path.GetDirectoryName(relative) ?? "";
        }
        return Path.GetFullPath(Path.Combine(outputRoot, relativeDirectory, fileName));
    }

    private static string GetDeclarationFileName(string sourcePath)
    {
        string fileName = Path.GetFileName(sourcePath);
        if (fileName.EndsWith(".mts", StringComparison.OrdinalIgnoreCase))
            return fileName[..^4] + ".d.mts";
        if (fileName.EndsWith(".cts", StringComparison.OrdinalIgnoreCase))
            return fileName[..^4] + ".d.cts";
        string extension = Path.GetExtension(fileName);
        return fileName[..^extension.Length] + ".d.ts";
    }

    private static bool IsPhysicalSource(string path) =>
        !path.StartsWith("stdlib:", StringComparison.Ordinal) &&
        !path.StartsWith("dotnet:", StringComparison.Ordinal) &&
        !path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase) &&
        !path.EndsWith(".d.mts", StringComparison.OrdinalIgnoreCase) &&
        !path.EndsWith(".d.cts", StringComparison.OrdinalIgnoreCase) &&
        (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".mts", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".cts", StringComparison.OrdinalIgnoreCase));

    private static string? FindCommonDirectory(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return null;
        string common = Path.GetDirectoryName(paths[0])!;
        foreach (string path in paths.Skip(1))
        {
            string directory = Path.GetDirectoryName(path)!;
            while (!IsUnder(directory, common))
            {
                string? parent = Path.GetDirectoryName(common);
                if (parent is null || parent == common)
                    return Path.GetPathRoot(common);
                common = parent;
            }
        }
        return common;
    }

    private static bool IsUnder(string candidate, string parent)
    {
        string relative = Path.GetRelativePath(parent, candidate);
        return relative == "." ||
               (!Path.IsPathRooted(relative) &&
                relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private sealed class FileEmitter(ParsedModule? module, TypeMap typeMap)
    {
        private readonly StringBuilder _builder = new();
        private readonly IReadOnlyDictionary<string, string> _dotNetImports =
            CollectDotNetImports(module?.Statements ?? []);
        private int _indent;

        public string Emit(IReadOnlyList<Stmt> statements)
        {
            foreach (Stmt statement in statements)
                EmitTopLevel(statement);
            return _builder.ToString().TrimEnd() + Environment.NewLine;
        }

        private void EmitTopLevel(Stmt statement)
        {
            switch (statement)
            {
                case Stmt.Import import when !import.ModulePath.StartsWith("dotnet:", StringComparison.Ordinal):
                    WriteLine(RenderImport(import));
                    break;
                case Stmt.ImportAlias alias:
                    WriteLine($"{(alias.IsExported ? "export " : "")}import {alias.AliasName.Lexeme} = {string.Join(".", alias.QualifiedPath.Select(token => token.Lexeme))};");
                    break;
                case Stmt.ImportRequire require when !require.ModulePath.StartsWith("dotnet:", StringComparison.Ordinal):
                    WriteLine($"{(require.IsExported ? "export " : "")}import {require.AliasName.Lexeme} = require({Quote(require.ModulePath)});");
                    break;
                case Stmt.Export export:
                    EmitExport(export);
                    break;
                default:
                    EmitDeclaration(statement, ExportKind.None, ResolveLocalType(statement));
                    break;
            }
        }

        private void EmitExport(Stmt.Export export)
        {
            if (export.ExportAssignment is not null)
            {
                TypeInfo type = module?.ExportAssignmentType ?? typeMap.Get(export.ExportAssignment)
                    ?? TypeInfo.Any.Shared;
                WriteLine($"declare const _export: {TypeInfoDeclarationRenderer.Render(type)};");
                WriteLine("export = _export;");
                return;
            }

            if (export.Declaration is not null)
            {
                TypeInfo? type = export.IsDefaultExport
                    ? module?.DefaultExportType
                    : GetExportedType(export.Declaration);
                EmitDeclaration(
                    export.Declaration,
                    export.IsDefaultExport ? ExportKind.Default : ExportKind.Named,
                    type);
                return;
            }

            if (export.DefaultExpr is not null)
            {
                TypeInfo type = module?.DefaultExportType ?? typeMap.Get(export.DefaultExpr)
                    ?? TypeInfo.Any.Shared;
                WriteLine($"declare const _default: {TypeInfoDeclarationRenderer.Render(type)};");
                WriteLine("export default _default;");
                return;
            }

            if (export.FromModulePath is not null)
            {
                if (export.NamespaceExportName is not null)
                {
                    WriteLine($"export {(export.IsTypeOnly ? "type " : "")}* as {RenderModuleExportName(export.NamespaceExportName)} from {Quote(export.FromModulePath)};");
                }
                else if (export.NamedExports is not null)
                {
                    WriteLine($"export {(export.IsTypeOnly ? "type " : "")}{{ {RenderExportSpecifiers(export.NamedExports)} }} from {Quote(export.FromModulePath)};");
                }
                else
                {
                    WriteLine($"export {(export.IsTypeOnly ? "type " : "")}* from {Quote(export.FromModulePath)};");
                }
                return;
            }

            if (export.NamedExports is not null)
                WriteLine($"export {(export.IsTypeOnly ? "type " : "")}{{ {RenderExportSpecifiers(export.NamedExports)} }};");
        }

        private TypeInfo? GetExportedType(Stmt declaration)
        {
            string? name = DeclarationName(declaration);
            return name is not null && module?.ExportedTypes.TryGetValue(name, out TypeInfo? type) == true
                ? type
                : ResolveLocalType(declaration);
        }

        private TypeInfo? ResolveLocalType(Stmt declaration) => declaration switch
        {
            Stmt.Function function => typeMap.GetFunctionType(function.Name.Lexeme),
            Stmt.Class @class => typeMap.GetClassType(@class.Name.Lexeme),
            Stmt.Var variable when variable.Initializer is not null => typeMap.Get(variable.Initializer),
            Stmt.Var variable when variable.HoistTypeInferenceInitializer is not null =>
                typeMap.Get(variable.HoistTypeInferenceInitializer),
            Stmt.Const constant => typeMap.Get(constant.Initializer),
            _ => null,
        };

        private void EmitDeclaration(Stmt statement, ExportKind exportKind, TypeInfo? resolvedType)
        {
            switch (statement)
            {
                case Stmt.Function function when !function.Name.Lexeme.StartsWith("__genArrow_", StringComparison.Ordinal):
                    EmitFunction(function, exportKind, resolvedType);
                    break;
                case Stmt.Class @class:
                    EmitClass(@class, exportKind, resolvedType);
                    break;
                case Stmt.Interface @interface:
                    EmitInterface(@interface, exportKind, resolvedType);
                    break;
                case Stmt.TypeAlias alias:
                    WriteLine($"{ExportPrefix(exportKind, ambient: false)}type {alias.Name.Lexeme}{RenderTypeParameters(alias.TypeParameters)} = {alias.TypeDefinition};");
                    break;
                case Stmt.Enum @enum:
                    EmitEnum(@enum, exportKind, resolvedType as TypeInfo.Enum);
                    break;
                case Stmt.Namespace @namespace:
                    EmitNamespace(@namespace, exportKind);
                    break;
                case Stmt.Var variable when !variable.Name.Lexeme.StartsWith("_dest", StringComparison.Ordinal):
                    EmitVariable(variable.Name.Lexeme, variable.TypeAnnotation,
                        variable.Initializer ?? variable.HoistTypeInferenceInitializer,
                        exportKind, isConst: false, isVar: variable.IsVar, resolvedType);
                    break;
                case Stmt.Const constant:
                    EmitVariable(constant.Name.Lexeme, constant.TypeAnnotation, constant.Initializer,
                        exportKind, isConst: true, isVar: false, resolvedType);
                    break;
                case Stmt.DeclareModule declareModule:
                    WriteLine($"declare module {Quote(declareModule.ModulePath)} {{");
                    WithIndent(() =>
                    {
                        foreach (Stmt member in declareModule.Members)
                            EmitDeclaration(member, ExportKind.None, ResolveLocalType(member));
                    });
                    WriteLine("}");
                    break;
                case Stmt.DeclareGlobal declareGlobal:
                    WriteLine("declare global {");
                    WithIndent(() =>
                    {
                        foreach (Stmt member in declareGlobal.Members)
                            EmitDeclaration(member, ExportKind.None, ResolveLocalType(member));
                    });
                    WriteLine("}");
                    break;
                case Stmt.Export nestedExport:
                    EmitExport(nestedExport);
                    break;
                case Stmt.ImportAlias alias:
                    WriteLine($"{(alias.IsExported ? "export " : "")}import {alias.AliasName.Lexeme} = {string.Join(".", alias.QualifiedPath.Select(token => token.Lexeme))};");
                    break;
                case Stmt.ImportRequire require:
                    WriteLine($"{(require.IsExported ? "export " : "")}import {require.AliasName.Lexeme} = require({Quote(require.ModulePath)});");
                    break;
            }
        }

        private void EmitVariable(
            string name,
            string? annotation,
            Expr? initializer,
            ExportKind exportKind,
            bool isConst,
            bool isVar,
            TypeInfo? resolvedType)
        {
            TypeInfo type = resolvedType ?? (initializer is null ? null : typeMap.Get(initializer))
                ?? TypeInfo.Any.Shared;
            string typeText = RenderAnnotation(annotation, type);
            string kind = isConst ? "const" : isVar ? "var" : "let";
            WriteLine($"{ExportPrefix(exportKind, ambient: true)}{kind} {name}: {typeText};");
        }

        private void EmitFunction(Stmt.Function function, ExportKind exportKind, TypeInfo? resolvedType)
        {
            switch (resolvedType)
            {
                case TypeInfo.OverloadedFunction overloaded:
                    foreach (TypeInfo.Function signature in overloaded.Signatures)
                        EmitFunctionSignature(function, exportKind, signature.ParamTypes, signature.ReturnType, null);
                    return;
                case TypeInfo.GenericOverloadedFunction overloaded:
                    foreach (TypeInfo.Function signature in overloaded.Signatures)
                        EmitFunctionSignature(function, exportKind, signature.ParamTypes, signature.ReturnType, overloaded.TypeParams);
                    return;
                case TypeInfo.GenericFunction generic:
                    EmitFunctionSignature(function, exportKind, generic.ParamTypes, generic.ReturnType, generic.TypeParams);
                    return;
                case TypeInfo.Function simple:
                    EmitFunctionSignature(function, exportKind, simple.ParamTypes, simple.ReturnType, null);
                    return;
                default:
                    EmitFunctionSignature(function, exportKind, null, null, null);
                    return;
            }
        }

        private void EmitFunctionSignature(
            Stmt.Function function,
            ExportKind exportKind,
            IReadOnlyList<TypeInfo>? resolvedParameters,
            TypeInfo? resolvedReturnType,
            IReadOnlyList<TypeInfo.TypeParameter>? resolvedTypeParameters)
        {
            string typeParameters = function.TypeParams is { Count: > 0 }
                ? RenderTypeParameters(function.TypeParams)
                : resolvedTypeParameters is { Count: > 0 }
                    ? $"<{string.Join(", ", resolvedTypeParameters.Select(TypeInfoDeclarationRenderer.RenderTypeParameterDeclaration))}>"
                    : "";
            string parameters = RenderParameters(function.Parameters, resolvedParameters);
            string returnType = RenderAnnotation(function.ReturnType, resolvedReturnType);
            WriteLine($"{ExportPrefix(exportKind, ambient: true, defaultAmbient: false)}function {function.Name.Lexeme}{typeParameters}({parameters}): {returnType};");
        }

        private void EmitClass(Stmt.Class @class, ExportKind exportKind, TypeInfo? resolvedType)
        {
            string modifiers = @class.IsAbstract ? "abstract " : "";
            string heritage = RenderHeritage(@class);
            WriteLine($"{ExportPrefix(exportKind, ambient: true, defaultAmbient: false)}{modifiers}class {@class.Name.Lexeme}{RenderTypeParameters(@class.TypeParams)}{heritage} {{");
            WithIndent(() =>
            {
                var methods = @class.Methods
                    .GroupBy(method => (method.Name.Lexeme, method.IsStatic, method.ComputedKey is not null))
                    .SelectMany(group => group.Any(method => method.Body is null)
                        ? group.Where(method => method.Body is null)
                        : group.Take(1));
                foreach (Stmt.Function method in methods)
                    EmitClassMethod(method, resolvedType);
                foreach (Stmt.Field field in @class.Fields)
                    EmitClassField(field, resolvedType);
                foreach (Stmt.Accessor accessor in @class.Accessors ?? [])
                    EmitAccessor(accessor, resolvedType);
                foreach (Stmt.AutoAccessor accessor in @class.AutoAccessors ?? [])
                    EmitAutoAccessor(accessor, resolvedType);
                foreach (Stmt.IndexSignature index in @class.IndexSignatures ?? [])
                    WriteLine($"[{index.KeyName.Lexeme}: {RenderIndexKeyType(index.KeyType)}]: {index.ValueType};");
            });
            WriteLine("}");
        }

        private void EmitClassMethod(Stmt.Function method, TypeInfo? classType)
        {
            TypeInfo? resolved = GetClassMemberType(classType, method.Name.Lexeme, method.IsStatic, method.IsPrivate);
            static (IReadOnlyList<TypeInfo>? Params, TypeInfo? Return, IReadOnlyList<TypeInfo.TypeParameter>? TypeParams)
                Signature(
                    IReadOnlyList<TypeInfo>? parameters,
                    TypeInfo? returnType,
                    IReadOnlyList<TypeInfo.TypeParameter>? typeParameters) =>
                (parameters, returnType, typeParameters);
            IEnumerable<(IReadOnlyList<TypeInfo>? Params, TypeInfo? Return, IReadOnlyList<TypeInfo.TypeParameter>? TypeParams)> signatures =
                resolved switch
                {
                    TypeInfo.OverloadedFunction overloaded => overloaded.Signatures.Select(signature =>
                        Signature(signature.ParamTypes, signature.ReturnType, null)),
                    TypeInfo.GenericOverloadedFunction overloaded => overloaded.Signatures.Select(signature =>
                        Signature(signature.ParamTypes, signature.ReturnType, overloaded.TypeParams)),
                    TypeInfo.GenericFunction generic =>
                        [Signature(generic.ParamTypes, generic.ReturnType, generic.TypeParams)],
                    TypeInfo.Function function =>
                        [Signature(function.ParamTypes, function.ReturnType, null)],
                    _ => [Signature(null, null, null)],
                };

            foreach (var signature in signatures)
            {
                string access = method.IsPrivate ? "" : RenderAccess(method.Access);
                string name = RenderMemberName(method.Name, method.ComputedKey, method.IsPrivate);
                string typeParameters = method.TypeParams is { Count: > 0 }
                    ? RenderTypeParameters(method.TypeParams)
                    : signature.TypeParams is { Count: > 0 }
                        ? $"<{string.Join(", ", signature.TypeParams.Select(TypeInfoDeclarationRenderer.RenderTypeParameterDeclaration))}>"
                        : "";
                string parameters = RenderParameters(method.Parameters, signature.Params);
                string staticModifier = method.IsStatic ? "static " : "";
                string abstractModifier = method.IsAbstract ? "abstract " : "";
                string returnType = RenderAnnotation(method.ReturnType, signature.Return);
                if (method.Name.Lexeme == "constructor")
                    WriteLine($"{access}constructor({parameters});");
                else
                    WriteLine($"{access}{staticModifier}{abstractModifier}{name}{typeParameters}({parameters}): {returnType};");
            }
        }

        private void EmitClassField(Stmt.Field field, TypeInfo? classType)
        {
            string access = field.IsPrivate ? "" : RenderAccess(field.Access);
            string staticModifier = field.IsStatic ? "static " : "";
            string readonlyModifier = field.IsReadonly ? "readonly " : "";
            string optional = field.IsOptional ? "?" : "";
            string name = RenderMemberName(field.Name, field.ComputedKey, field.IsPrivate);
            TypeInfo? resolved = GetClassFieldType(classType, field.Name.Lexeme, field.IsStatic, field.IsPrivate)
                ?? (field.Initializer is null ? null : typeMap.Get(field.Initializer));
            string type = RenderAnnotation(field.TypeAnnotation, resolved);
            WriteLine($"{access}{staticModifier}{readonlyModifier}{name}{optional}: {type};");
        }

        private void EmitAccessor(Stmt.Accessor accessor, TypeInfo? classType)
        {
            string access = RenderAccess(accessor.Access);
            string staticModifier = accessor.IsStatic ? "static " : "";
            string name = RenderMemberName(accessor.Name, accessor.ComputedKey, isPrivate: false);
            if (accessor.Kind.Lexeme == "get")
            {
                TypeInfo? type = GetClassGetterType(classType, accessor.Name.Lexeme);
                WriteLine($"{access}{staticModifier}get {name}(): {RenderAnnotation(accessor.ReturnType, type)};");
            }
            else
            {
                Stmt.Parameter parameter = accessor.SetterParam!;
                string type = RenderAnnotation(
                    parameter.Type,
                    GetClassSetterType(classType, accessor.Name.Lexeme));
                WriteLine($"{access}{staticModifier}set {name}({parameter.Name.Lexeme}: {type});");
            }
        }

        private void EmitAutoAccessor(Stmt.AutoAccessor accessor, TypeInfo? classType)
        {
            TypeInfo? resolved = GetClassFieldType(classType, accessor.Name.Lexeme, accessor.IsStatic, isPrivate: false)
                ?? (accessor.Initializer is null ? null : typeMap.Get(accessor.Initializer));
            WriteLine($"{RenderAccess(accessor.Access)}{(accessor.IsStatic ? "static " : "")}accessor {accessor.Name.Lexeme}: " +
                      $"{RenderAnnotation(accessor.TypeAnnotation, resolved)};");
        }

        private void EmitInterface(Stmt.Interface @interface, ExportKind exportKind, TypeInfo? resolvedType)
        {
            string extends = @interface.Extends is { Count: > 0 }
                ? $" extends {string.Join(", ", @interface.Extends)}"
                : "";
            WriteLine($"{ExportPrefix(exportKind, ambient: false)}interface {@interface.Name.Lexeme}{RenderTypeParameters(@interface.TypeParams)}{extends} {{");
            WithIndent(() =>
            {
                foreach (Stmt.CallSignature call in @interface.CallSignatures ?? [])
                    WriteLine($"{RenderTypeParameters(call.TypeParams)}({RenderParameters(call.Parameters, null)}): {call.ReturnType};");
                foreach (Stmt.ConstructorSignature constructor in @interface.ConstructorSignatures ?? [])
                    WriteLine($"new {RenderTypeParameters(constructor.TypeParams)}({RenderParameters(constructor.Parameters, null)}): {constructor.ReturnType};");
                foreach (Stmt.InterfaceMember member in @interface.Members)
                {
                    string readOnly = member.IsReadonly ? "readonly " : "";
                    string optional = member.IsOptional ? "?" : "";
                    TypeInfo? resolvedMember = GetInterfaceMemberType(resolvedType, member.Name.Lexeme);
                    string functionType = resolvedMember switch
                    {
                        TypeInfo.OverloadSet { Signatures.Count: > 0 } overload =>
                            // The checker merges the preparatory interface surface
                            // before the authoritative one; the latter is last and
                            // carries resolved recursive generic return types.
                            TypeInfoDeclarationRenderer.Render(overload.Signatures[^1]),
                        null => member.Type,
                        _ => TypeInfoDeclarationRenderer.Render(resolvedMember),
                    };
                    if (member.IsMethod && TrySplitFunctionType(functionType, out string? parameters, out string? result))
                        WriteLine($"{member.Name.Lexeme}{optional}{parameters}: {result};");
                    else
                        WriteLine($"{readOnly}{member.Name.Lexeme}{optional}: " +
                                  $"{RenderAnnotation(member.Type, resolvedMember)};");
                }
                foreach (Stmt.IndexSignature index in @interface.IndexSignatures ?? [])
                    WriteLine($"[{index.KeyName.Lexeme}: {RenderIndexKeyType(index.KeyType)}]: {index.ValueType};");
            });
            WriteLine("}");
        }

        private void EmitEnum(Stmt.Enum @enum, ExportKind exportKind, TypeInfo.Enum? resolved)
        {
            WriteLine($"{ExportPrefix(exportKind, ambient: true)}{(@enum.IsConst ? "const " : "")}enum {@enum.Name.Lexeme} {{");
            WithIndent(() =>
            {
                foreach (Stmt.EnumMember member in @enum.Members)
                {
                    string? value = RenderConstantExpression(member.Value);
                    if (value is null && resolved?.Members.TryGetValue(member.Name.Lexeme, out object? checkedValue) == true)
                        value = RenderConstant(checkedValue);
                    WriteLine($"{member.Name.Lexeme}{(value is null ? "" : $" = {value}")},");
                }
            });
            WriteLine("}");
        }

        private void EmitNamespace(Stmt.Namespace @namespace, ExportKind exportKind)
        {
            WriteLine($"{ExportPrefix(exportKind, ambient: true)}namespace {@namespace.Name.Lexeme} {{");
            WithIndent(() =>
            {
                foreach (Stmt member in @namespace.Members)
                    EmitDeclaration(member, ExportKind.None, ResolveLocalType(member));
            });
            WriteLine("}");
        }

        private static string RenderImport(Stmt.Import import)
        {
            string typePrefix = import.IsTypeOnly ? "type " : "";
            var bindings = new List<string>();
            if (import.DefaultImport is not null)
                bindings.Add(import.DefaultImport.Lexeme);
            if (import.NamespaceImport is not null)
                bindings.Add($"* as {import.NamespaceImport.Lexeme}");
            if (import.NamedImports is { Count: > 0 })
            {
                string named = string.Join(", ", import.NamedImports.Select(specifier =>
                    $"{(specifier.IsTypeOnly ? "type " : "")}{RenderModuleExportName(specifier.Imported)}" +
                    $"{(specifier.LocalName is null ? "" : $" as {specifier.LocalName.Lexeme}")}"));
                bindings.Add($"{{ {named} }}");
            }
            return bindings.Count == 0
                ? $"import {Quote(import.ModulePath)};"
                : $"import {typePrefix}{string.Join(", ", bindings)} from {Quote(import.ModulePath)};";
        }

        private static string RenderExportSpecifiers(IEnumerable<Stmt.ExportSpecifier> specifiers) =>
            string.Join(", ", specifiers.Select(specifier =>
                $"{(specifier.IsTypeOnly ? "type " : "")}" +
                (specifier.ExportedName is null
                    ? RenderModuleExportName(specifier.LocalName)
                    : $"{RenderModuleExportName(specifier.LocalName)} as {RenderModuleExportName(specifier.ExportedName)}")));

        private static string RenderModuleExportName(Token token) =>
            token.Type == TokenType.STRING ? Quote(token.Lexeme) : token.Lexeme;

        private string RenderParameters(
            IReadOnlyList<Stmt.Parameter> parameters,
            IReadOnlyList<TypeInfo>? resolvedTypes)
        {
            return string.Join(", ", parameters.Select((parameter, index) =>
            {
                string access = parameter.IsParameterProperty && parameter.Access is not null
                    ? RenderAccess(parameter.Access.Value)
                    : "";
                string readOnly = parameter.IsParameterProperty && parameter.IsReadonly ? "readonly " : "";
                string rest = parameter.IsRest ? "..." : "";
                string optional = parameter.IsOptional || parameter.DefaultValue is not null ? "?" : "";
                TypeInfo? resolved = resolvedTypes is not null && index < resolvedTypes.Count
                    ? resolvedTypes[index]
                    : null;
                string type = RenderAnnotation(parameter.Type, resolved);
                return $"{access}{readOnly}{rest}{parameter.Name.Lexeme}{optional}: {type}";
            }));
        }

        private static string RenderTypeParameters(IReadOnlyList<TypeParam>? parameters)
        {
            if (parameters is not { Count: > 0 })
                return "";
            return $"<{string.Join(", ", parameters.Select(parameter =>
            {
                string variance = parameter.Variance switch
                {
                    TypeParameterVariance.In => "in ",
                    TypeParameterVariance.Out => "out ",
                    TypeParameterVariance.InOut => "in out ",
                    _ => "",
                };
                return $"{variance}{(parameter.IsConst ? "const " : "")}{parameter.Name.Lexeme}" +
                       $"{(parameter.Constraint is null ? "" : $" extends {parameter.Constraint}")}" +
                       $"{(parameter.Default is null ? "" : $" = {parameter.Default}")}";
            }))}>";
        }

        private static string RenderHeritage(Stmt.Class @class)
        {
            var parts = new List<string>();
            string? superclass = Expr.GetSuperclassName(@class.SuperclassExpr);
            if (superclass is not null)
            {
                string typeArguments = @class.SuperclassTypeArgs is { Count: > 0 }
                    ? $"<{string.Join(", ", @class.SuperclassTypeArgs)}>"
                    : "";
                parts.Add($"extends {superclass}{typeArguments}");
            }
            if (@class.Interfaces is { Count: > 0 })
            {
                var interfaces = @class.Interfaces.Select((token, index) =>
                {
                    string typeArguments = @class.InterfaceTypeArgs is not null &&
                                           index < @class.InterfaceTypeArgs.Count &&
                                           @class.InterfaceTypeArgs[index].Count > 0
                        ? $"<{string.Join(", ", @class.InterfaceTypeArgs[index])}>"
                        : "";
                    return token.Lexeme + typeArguments;
                });
                parts.Add($"implements {string.Join(", ", interfaces)}");
            }
            return parts.Count == 0 ? "" : " " + string.Join(" ", parts);
        }

        private static string RenderMemberName(Token name, Expr? computedKey, bool isPrivate)
        {
            if (computedKey is not null)
                return $"[{RenderNameExpression(computedKey)}]";
            return isPrivate ? $"#{name.Lexeme.TrimStart('#')}" : name.Lexeme;
        }

        private static string RenderNameExpression(Expr expression) => expression switch
        {
            Expr.Variable variable => variable.Name.Lexeme,
            Expr.Get get => $"{RenderNameExpression(get.Object)}.{get.Name.Lexeme}",
            Expr.GetIndex get => $"{RenderNameExpression(get.Object)}[{RenderNameExpression(get.Index)}]",
            Expr.Literal literal => RenderConstant(literal.Value) ?? "undefined",
            _ => throw new DeclarationEmitException(
                $"Computed declaration name using '{expression.GetType().Name}' cannot currently be emitted."),
        };

        private static string? RenderConstantExpression(Expr? expression) => expression switch
        {
            null => null,
            Expr.Literal literal => RenderConstant(literal.Value),
            Expr.Variable variable => variable.Name.Lexeme,
            Expr.Get get => $"{RenderNameExpression(get.Object)}.{get.Name.Lexeme}",
            Expr.Grouping grouping => $"({RenderConstantExpression(grouping.Expression)})",
            Expr.Unary unary => $"{unary.Operator.Lexeme}{RenderConstantExpression(unary.Right)}",
            Expr.Binary binary =>
                $"{RenderConstantExpression(binary.Left)} {binary.Operator.Lexeme} {RenderConstantExpression(binary.Right)}",
            _ => null,
        };

        private static string? RenderConstant(object? value) => value switch
        {
            null => "null",
            string text => Quote(text),
            bool boolean => boolean ? "true" : "false",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            System.Numerics.BigInteger number => $"{number.ToString(CultureInfo.InvariantCulture)}n",
            _ => null,
        };

        private static bool TrySplitFunctionType(string type, out string? parameters, out string? result)
        {
            int arrow = type.LastIndexOf("=>", StringComparison.Ordinal);
            if (arrow < 0)
            {
                parameters = null;
                result = null;
                return false;
            }
            parameters = type[..arrow].Trim();
            result = type[(arrow + 2)..].Trim();
            return true;
        }

        private string RenderAnnotation(string? annotation, TypeInfo? resolved)
        {
            foreach (var (name, modulePath) in _dotNetImports)
            {
                if ((annotation is not null && ContainsIdentifier(annotation, name)) ||
                    (annotation is null && HasNamedType(resolved, name)))
                {
                    throw new DeclarationEmitException(
                        $"Public declaration type '{name}' comes from CLR import '{modulePath}' " +
                        "and is not portable to TypeScript consumers.");
                }
            }
            string rendered = TypeInfoDeclarationRenderer.Render(resolved ?? TypeInfo.Any.Shared);
            return annotation ?? rendered;
        }

        private static IReadOnlyDictionary<string, string> CollectDotNetImports(
            IReadOnlyList<Stmt> statements)
        {
            var imports = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Stmt.Import import in statements.OfType<Stmt.Import>())
            {
                if (!import.ModulePath.StartsWith("dotnet:", StringComparison.Ordinal))
                    continue;
                foreach (Stmt.ImportSpecifier specifier in import.NamedImports ?? [])
                {
                    string localName = specifier.LocalName?.Lexeme ?? specifier.Imported.Lexeme;
                    imports[localName] = import.ModulePath;
                }
            }
            return imports;
        }

        private static bool ContainsIdentifier(string text, string identifier)
        {
            int start = 0;
            while ((start = text.IndexOf(identifier, start, StringComparison.Ordinal)) >= 0)
            {
                int end = start + identifier.Length;
                bool leftBoundary = start == 0 || !IsIdentifierPart(text[start - 1]);
                bool rightBoundary = end == text.Length || !IsIdentifierPart(text[end]);
                if (leftBoundary && rightBoundary)
                    return true;
                start = end;
            }
            return false;
        }

        private static bool IsIdentifierPart(char character) =>
            char.IsLetterOrDigit(character) || character is '_' or '$';

        private static bool HasNamedType(TypeInfo? type, string name) => type switch
        {
            TypeInfo.Class @class => @class.Name == name,
            TypeInfo.MutableClass @class => @class.Name == name,
            TypeInfo.GenericClass @class => @class.Name == name,
            TypeInfo.Instance instance => HasNamedType(instance.ResolvedClassType, name),
            TypeInfo.InstantiatedGeneric generic => HasNamedType(generic.GenericDefinition, name),
            _ => false,
        };

        private static TypeInfo? GetClassMemberType(TypeInfo? type, string name, bool isStatic, bool isPrivate) =>
            type switch
            {
                TypeInfo.Class @class when isPrivate && isStatic =>
                    @class.StaticPrivateMethodTypes.GetValueOrDefault(name),
                TypeInfo.Class @class when isPrivate =>
                    @class.PrivateMethodTypes.GetValueOrDefault(name),
                TypeInfo.Class @class when isStatic => @class.StaticMethods.GetValueOrDefault(name),
                TypeInfo.Class @class => @class.Methods.GetValueOrDefault(name),
                TypeInfo.GenericClass @class when isPrivate && isStatic =>
                    @class.StaticPrivateMethodTypes.GetValueOrDefault(name),
                TypeInfo.GenericClass @class when isPrivate =>
                    @class.PrivateMethodTypes.GetValueOrDefault(name),
                TypeInfo.GenericClass @class when isStatic => @class.StaticMethods.GetValueOrDefault(name),
                TypeInfo.GenericClass @class => @class.Methods.GetValueOrDefault(name),
                _ => null,
            };

        private static TypeInfo? GetInterfaceMemberType(TypeInfo? type, string name) => type switch
        {
            TypeInfo.Interface @interface => @interface.Members.GetValueOrDefault(name),
            TypeInfo.GenericInterface @interface => @interface.Members.GetValueOrDefault(name),
            _ => null,
        };

        private static TypeInfo? GetClassFieldType(TypeInfo? type, string name, bool isStatic, bool isPrivate) =>
            type switch
            {
                TypeInfo.Class @class when isPrivate && isStatic =>
                    @class.StaticPrivateFieldTypes.GetValueOrDefault(name),
                TypeInfo.Class @class when isPrivate =>
                    @class.PrivateFieldTypes.GetValueOrDefault(name),
                TypeInfo.Class @class when isStatic => @class.StaticProperties.GetValueOrDefault(name),
                TypeInfo.Class @class => @class.FieldTypes.GetValueOrDefault(name),
                TypeInfo.GenericClass @class when isPrivate && isStatic =>
                    @class.StaticPrivateFieldTypes.GetValueOrDefault(name),
                TypeInfo.GenericClass @class when isPrivate =>
                    @class.PrivateFieldTypes.GetValueOrDefault(name),
                TypeInfo.GenericClass @class when isStatic => @class.StaticProperties.GetValueOrDefault(name),
                TypeInfo.GenericClass @class => @class.FieldTypes.GetValueOrDefault(name),
                _ => null,
            };

        private static TypeInfo? GetClassGetterType(TypeInfo? type, string name) => type switch
        {
            TypeInfo.Class @class => @class.Getters.GetValueOrDefault(name),
            TypeInfo.GenericClass @class => @class.Getters.GetValueOrDefault(name),
            _ => null,
        };

        private static TypeInfo? GetClassSetterType(TypeInfo? type, string name) => type switch
        {
            TypeInfo.Class @class => @class.Setters.GetValueOrDefault(name),
            TypeInfo.GenericClass @class => @class.Setters.GetValueOrDefault(name),
            _ => null,
        };

        private static string? DeclarationName(Stmt declaration) => declaration switch
        {
            Stmt.Function function => function.Name.Lexeme,
            Stmt.Class @class => @class.Name.Lexeme,
            Stmt.Interface @interface => @interface.Name.Lexeme,
            Stmt.TypeAlias alias => alias.Name.Lexeme,
            Stmt.Enum @enum => @enum.Name.Lexeme,
            Stmt.Namespace @namespace => @namespace.Name.Lexeme,
            Stmt.Var variable => variable.Name.Lexeme,
            Stmt.Const constant => constant.Name.Lexeme,
            _ => null,
        };

        private static string ExportPrefix(
            ExportKind kind,
            bool ambient,
            bool defaultAmbient = true) => kind switch
        {
            ExportKind.Default => ambient && defaultAmbient ? "export default declare " : "export default ",
            ExportKind.Named => ambient ? "export declare " : "export ",
            _ => ambient ? "declare " : "",
        };

        private static string RenderAccess(AccessModifier access) => access switch
        {
            AccessModifier.Private => "private ",
            AccessModifier.Protected => "protected ",
            _ => "public ",
        };

        private static string RenderIndexKeyType(TokenType keyType) => keyType switch
        {
            TokenType.TYPE_NUMBER => "number",
            TokenType.TYPE_SYMBOL => "symbol",
            _ => "string",
        };

        private static string Quote(string text) => JsonStringEscaper.Quote(text);

        private void WriteLine(string line)
        {
            if (_builder.Length > 0 && _builder[^1] != '\n')
                _builder.AppendLine();
            _builder.Append(' ', _indent * 4).AppendLine(line);
        }

        private void WithIndent(Action action)
        {
            _indent++;
            try { action(); }
            finally { _indent--; }
        }
    }

    private enum ExportKind
    {
        None,
        Named,
        Default,
    }
}
