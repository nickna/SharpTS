using SharpTS.Configuration;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.Modules;

public sealed class EditorParseTargetResolverTests
{
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpts-editor-target-" + Guid.NewGuid().ToString("N")));

    private static ModuleResolver Resolver(string entry, IReadOnlyDictionary<string, string> files,
        EditorParseTarget? target = null, ModuleResolutionOptions? resolution = null,
        TypeScriptProgramOptions? program = null) => new(entry, resolution ?? ModuleResolutionOptions.Default,
            files, program ?? TypeScriptProgramOptions.Default with { NoLib = true, Types = [] })
        {
            RecoverParseErrors = true,
            CaptureEditorSyntax = true,
            EditorParseTarget = target,
        };

    [Fact]
    public void OnlyTheExactCanonicalTargetGetsFreshRecoveryAndChecking()
    {
        string root = Root();
        string entry = Path.Combine(root, "main.ts");
        string dependency = Path.Combine(root, "other", "main.ts");
        const string source = "import { model } from './other/main'; model.;";
        var files = new Dictionary<string, string>
        {
            [entry] = source,
            [dependency] = "export class Model { field: number = 1; } export const model = new Model(); model.;",
        };
        var baselineResolver = Resolver(entry, files);
        var baseline = baselineResolver.LoadProgram(entry);
        var baselineDependency = baselineResolver.GetCachedModule(dependency)!;
        int originalSpanCount = baseline.Document!.Spans.Count;
        int originalSyntaxCount = baseline.Document.EditorSyntax!.Count;
        var target = new EditorParseTarget(Path.Combine(root, "other", "..", "main.ts"),
            source.LastIndexOf('.') + 1, EditorQueryKind.Completion, EditorRecoveryPolicy.Default);
        var recoveredResolver = Resolver(entry, files, target);
        var recovered = recoveredResolver.LoadProgram(entry);
        var other = recoveredResolver.GetCachedModule(dependency)!;

        Assert.NotSame(baseline.Document, recovered.Document);
        Assert.NotSame(baselineDependency.Document, other.Document);
        Assert.Equal(source, recovered.Document!.Text);
        Assert.True(Assert.Single(recovered.Document.EditorSyntax!.Members).IsRecovered);
        Assert.DoesNotContain(other.Document!.EditorSyntax!.Members, member => member.IsRecovered);
        Assert.Contains(recovered.ParseDiagnostics, diagnostic => diagnostic.Message.Contains("Identifier expected", StringComparison.Ordinal));
        Assert.NotEmpty(other.ParseDiagnostics);
        Assert.Equal(originalSpanCount, baseline.Document.Spans.Count);
        Assert.Equal(originalSyntaxCount, baseline.Document.EditorSyntax.Count);
        Assert.DoesNotContain(baseline.Statements, statement => recovered.Statements.Any(candidate => ReferenceEquals(candidate, statement)));

        var checker = new TypeChecker().WithEditorMetadata();
        checker.CheckModules(recoveredResolver.GetModulesInOrder(recovered), recoveredResolver);
        var member = Assert.Single(recovered.Document.EditorSyntax.Members);
        var facts = checker.EditorFacts.Freeze();
        Assert.Equal(EditorFactAvailability.Available, facts.GetOccurrence(recovered.Document, member.Receiver)!.Availability);
        Assert.Contains(facts.GetReceiverMembers(recovered.Document, member.Receiver).Members, candidate => candidate.Name == "field");
        Assert.Equal(originalSpanCount, baseline.Document.Spans.Count);
        Assert.Equal(originalSyntaxCount, baseline.Document.EditorSyntax.Count);
    }

    [Fact]
    public void OriginalReferenceDirectivesAndJsxPragmasStillDriveTheProgram()
    {
        string root = Root();
        string entry = Path.Combine(root, "main.tsx");
        string global = Path.Combine(root, "global.d.ts");
        string custom = Path.Combine(root, "node_modules", "@types", "custom", "index.d.ts");
        const string source = "/// <reference path=\"./global.d.ts\" />\n" +
            "/// <reference lib=\"es2015.promise\" />\n/// <reference types=\"custom\" />\n" +
            "/// <reference no-default-lib=\"true\" />\n/** @jsx custom.make */\n" +
            "const view = <Box />; receiver.;";
        var resolver = Resolver(entry, new Dictionary<string, string>
        {
            [entry] = source,
            [global] = "declare const receiver: { field: number };",
            [custom] = "declare const customValue: number;",
        }, new(entry, source.LastIndexOf('.') + 1, EditorQueryKind.Completion, EditorRecoveryPolicy.Default));
        var module = resolver.LoadProgram(entry);
        var graph = resolver.GetModulesInOrder(module);

        Assert.True(module.NoDefaultLib);
        Assert.Single(module.PathReferences);
        Assert.Contains(graph, item => item.Path == global && item.IsDeclarationFile);
        Assert.Contains(graph, item => item.Path == custom && item.IsDeclarationFile);
        Assert.Contains(graph, item => item.Path == "typescript-lib:lib.es2015.promise.d.ts");
        var view = Assert.IsType<Stmt.Const>(module.Statements.First(statement => statement is Stmt.Const));
        Assert.Equal("custom.make", Assert.IsType<Expr.Call>(view.Initializer).JsxOrigin!.FactoryName);
        Assert.True(Assert.Single(module.Document!.EditorSyntax!.Members).IsRecovered);
    }

    [Fact]
    public void TargetParseUsesExistingPathAliasesAndDeclarationPreference()
    {
        string root = Root();
        string entry = Path.Combine(root, "main.ts");
        string declaration = Path.Combine(root, "models", "model.d.ts");
        const string source = "import { model } from '@model'; model.;";
        var options = new ModuleResolutionOptions(ModuleResolutionMode.Bundler, root,
            new Dictionary<string, IReadOnlyList<string>> { ["@model"] = [Path.Combine(root, "models", "model")] });
        var resolver = Resolver(entry, new Dictionary<string, string>
        {
            [entry] = source,
            [declaration] = "export declare const model: { field: number };",
            [Path.Combine(root, "models", "model.js")] = "export const model = { field: 'wrong' };",
        }, new(entry, source.LastIndexOf('.') + 1, EditorQueryKind.Completion, EditorRecoveryPolicy.Default), options);
        var module = resolver.LoadProgram(entry);

        Assert.Equal(declaration, Assert.Single(module.Dependencies).Path);
        Assert.True(Assert.Single(module.Dependencies).IsDeclarationFile);
        Assert.True(Assert.Single(module.Document!.EditorSyntax!.Members).IsRecovered);
    }

    [Fact]
    public void TargetParserReceivesResolvedLegacyDecoratorMode()
    {
        string entry = Path.Combine(Root(), "main.ts");
        const string source = "@decorator class Model {} const model = new Model(); model.;";
        var resolver = Resolver(entry, new Dictionary<string, string> { [entry] = source },
            new(entry, source.LastIndexOf('.') + 1, EditorQueryKind.Completion, EditorRecoveryPolicy.Default));
        var module = resolver.LoadProgram(entry, DecoratorMode.Legacy);

        Assert.NotEmpty(Assert.IsType<Stmt.Class>(module.Statements[0]).Decorators!);
        Assert.Single(module.ParseDiagnostics);
        Assert.True(Assert.Single(module.Document!.EditorSyntax!.Members).IsRecovered);
    }

    [Fact]
    public void ExplicitTargetDoesNotChangeFailFastResolverPolicy()
    {
        string entry = Path.Combine(Root(), "main.ts");
        const string source = "receiver.;";
        var resolver = Resolver(entry, new Dictionary<string, string> { [entry] = source },
            new(entry, "receiver.".Length, EditorQueryKind.Completion, EditorRecoveryPolicy.Default));
        resolver.RecoverParseErrors = false;
        Assert.ThrowsAny<Exception>(() => resolver.LoadProgram(entry));
    }

    [Fact]
    public void TargetLoadingPreservesCancellationInsteadOfReturningRecovery()
    {
        string entry = Path.Combine(Root(), "main.ts");
        const string source = "receiver.;";
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var resolver = Resolver(entry, new Dictionary<string, string> { [entry] = source },
            new(entry, "receiver.".Length, EditorQueryKind.Completion, EditorRecoveryPolicy.Default))
            .WithCancellation(cancellation.Token);
        var error = Assert.Throws<OperationCanceledException>(() => resolver.LoadProgram(entry));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }
}
