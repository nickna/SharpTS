using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class RawCallStorageAnalysisTests
{
    [Fact]
    public void CharCodeAtNativeProofDoesNotAdmitDynamicOrUserDefinedReceivers()
    {
        // A String-only program has no dynamic source destination and retains native storage.
        var cleanStatements = Parse("function readString(input: string, index: number): number {\n    let code: number = input.charCodeAt(index);\n    return code;\n}");
        var cleanMap = new TypeChecker().Check(cleanStatements);
        var cleanNative = Variable(Function(cleanStatements, "readString").Body!, "code");
        RawCallStorageAnalysis.Capture(cleanStatements, cleanMap)
            .Analyze(cleanStatements, new RuntimeFeatureDetector().Detect(cleanStatements, cleanMap));
        AssertLocal(cleanMap, cleanNative, expectedRaw: false);

        var statements = Parse("""
            function readString(input: string, index: number): number {
                let code: number = input.charCodeAt(index);
                return code;
            }
            function readAny(input: any): number {
                let code: number = input.charCodeAt(0);
                return code;
            }
            class User {
                charCodeAt(index: number): number { return null as any; }
            }
            function readUser(input: User): number {
                let code: number = input.charCodeAt(0);
                return code;
            }
            """);
        var map = new TypeChecker().Check(statements);
        var native = Variable(Function(statements, "readString").Body!, "code");
        var dynamic = Variable(Function(statements, "readAny").Body!, "code");
        var user = Variable(Function(statements, "readUser").Body!, "code");

        RawCallStorageAnalysis.Capture(statements, map)
            .Analyze(statements, new RuntimeFeatureDetector().Detect(statements, map));

        // The neighboring Any call may reach a source String parameter. Its existing
        // negative slot fact must veto the otherwise type-directed charCodeAt proof.
        Assert.True(map.IsRawNumericStorage(Function(statements, "readString").Parameters[0].Name));
        AssertLocal(map, native, expectedRaw: true);
        Assert.True(map.IsUndefinedReachableNumericLocal(dynamic));
        Assert.True(map.IsUndefinedReachableNumericLocal(dynamic.Initializer!));
        AssertLocal(map, user, expectedRaw: true);
    }

    [Fact]
    public void BindingIdentityAndRewriteFlagsSurviveCheckerReuse()
    {
        foreach (bool recover in new[] { false, true })
        {
            var statements = Parse(RawCallStorageValueTests.BindingIsolationAndConversions);
            var checker = new TypeChecker();
            TypeMap map;
            if (recover)
            {
                var result = checker.CheckWithRecovery(statements);
                Assert.Empty(result.Diagnostics);
                map = result.TypeMap;
            }
            else map = checker.Check(statements);

            var outer = Function(statements, "outer");
            var outerBody = outer.Body!;
            var outerParameter = Assert.Single(outer.Parameters);
            var childParameter = Assert.Single(Function(outerBody, "child").Parameters);
            var blocks = outerBody.OfType<Stmt.Block>().ToArray();
            var clean = Variable(blocks[0].Statements, "value");
            var raw = Variable(blocks[1].Statements, "value");
            var captured = Variable(outerBody, "captured");
            var mutate = Assert.IsType<Expr.ArrowFunction>(Assert.Single(
                outerBody.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == "mutate").Initializer);
            var write = Assert.IsType<Expr.Assign>(Assert.IsType<Stmt.Expression>(
                Assert.Single(mutate.BlockBody!)).Expr);
            var outerBinding = Assert.IsType<BindingSymbol>(map.GetValueBinding(outerParameter.Name));
            var cleanBinding = Assert.IsType<BindingSymbol>(map.GetValueBinding(clean.Name));
            var rawBinding = Assert.IsType<BindingSymbol>(map.GetValueBinding(raw.Name));
            var childBinding = Assert.IsType<BindingSymbol>(map.GetValueBinding(childParameter.Name));
            var capturedBinding = Assert.IsType<BindingSymbol>(map.GetValueBinding(captured.Name));
            Assert.NotSame(outerBinding, cleanBinding);
            Assert.NotSame(outerBinding, rawBinding);
            Assert.NotSame(cleanBinding, rawBinding);
            Assert.NotSame(outerBinding, childBinding);
            Assert.Same(capturedBinding, map.GetValueBinding(write.Name));
            checker.Bindings.Clear();
            Assert.Same(outerBinding, map.GetValueBinding(outerParameter.Name));
            Assert.Same(rawBinding, map.GetValueBinding(raw.Name));
            Assert.Same(capturedBinding, map.GetValueBinding(write.Name));

            CompileAndVerify(statements, map);
            AssertLocal(map, raw, expectedRaw: true);
            AssertLocal(map, captured, expectedRaw: true);
            AssertLocal(map, clean, expectedRaw: false);
            Assert.False(map.IsUndefinedReachableNumericParam(outerParameter));
            Assert.False(map.IsUndefinedReachableNumericParam(childParameter));
            Assert.False(map.IsRawCallStorage(outerParameter.Name));
            Assert.False(map.IsRawCallStorage(childParameter.Name));
        }

        AssertParameterFlags();
        AssertVirtualSlotFlags();
        AssertActualRewriteFlags();
        AssertModuleIsolation();
    }

    [Fact]
    public void RawCallEvidenceDefeatsPreexistingAccumulatorPromotion()
    {
        var statements = Parse(RawCallStorageValueTests.CustomIteratorRawStorage);
        var checker = new TypeChecker();
        var map = checker.Check(statements);
        var unsafeBody = Function(statements, "unsafe").Body!;
        var unsafeTotal = Variable(unsafeBody, "total");
        var later = Assert.IsType<Stmt.Block>(Assert.Single(unsafeBody.OfType<Stmt.If>()).ThenBranch);
        var copied = Assert.Single(later.Statements.OfType<Stmt.Const>());
        var safeTotal = Variable(Function(statements, "safe").Body!, "total");
        var loop = Assert.Single(unsafeBody.OfType<Stmt.ForOf>());
        Assert.IsType<Expr.Assign>(Assert.IsType<Stmt.Expression>(loop.Body).Expr);
        map.MarkStableCustomIteratorNumericAccumulator(unsafeTotal.Name);
        Assert.True(map.IsStableCustomIteratorNumericAccumulator(unsafeTotal.Name));
        checker.Bindings.Clear();

        CompileAndVerify(statements, map);
        // A previously published positive hint cannot erase the newer raw-call evidence.
        Assert.True(map.IsStableCustomIteratorNumericAccumulator(unsafeTotal.Name));
        Assert.True(map.IsRawCallStorage(unsafeTotal.Name));
        Assert.True(map.IsRawCallStorage(copied.Name));
        AssertLocal(map, unsafeTotal, expectedRaw: true);
        Assert.True(map.IsUndefinedReachableNumericLocal(copied));
        Assert.True(map.IsUndefinedReachableNumericLocal(copied.Initializer));
        Assert.False(map.IsRawCallStorage(safeTotal.Name));
        Assert.True(map.IsStableCustomIteratorNumericAccumulator(safeTotal.Name));
    }

    private static void AssertParameterFlags()
    {
        var statements = Parse(RawCallStorageValueTests.StorageAndOwners);
        var checker = new TypeChecker();
        var map = checker.Check(statements);
        var parameters = Function(statements, "observe").Parameters;
        var expression = Assert.IsType<Expr.ArrowFunction>(Assert.Single(
            statements.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == "expression").Initializer);
        checker.Bindings.Clear();
        CompileAndVerify(statements, map);
        Assert.All(parameters, parameter =>
        {
            Assert.True(map.IsUndefinedReachableNumericParam(parameter));
            Assert.True(map.IsRawCallStorage(parameter.Name));
        });
        Assert.True(map.IsUndefinedReachableNumericParam(Assert.Single(expression.Parameters)));
    }

    private static void AssertActualRewriteFlags()
    {
        var statements = Parse(RawCallStorageValueTests.RewrittenAndDefaultOwners);
        var checker = new TypeChecker();
        var map = checker.Check(statements);
        var make = Function(statements, "make");
        Assert.True(make.IsGenerator);
        var sourceParameter = Assert.Single(Function(make.Body!, "captured").Parameters);
        var seed = Assert.Single(make.Parameters);
        var raw = Function(statements, "raw");
        var returnStatement = Assert.Single(raw.Body!.OfType<Stmt.Return>());
        var priorReturn = returnStatement.Value!;
        Assert.True(map.IsUndefinedReachableReturn(priorReturn));
        var returnClone = priorReturn with { };
        Assert.Null(map.Get(returnClone));
        checker.Bindings.Clear();
        var analysis = RawCallStorageAnalysis.Capture(statements, map);
        // Preserve an existing checker return hint across an equivalent clone even
        // when the clone has no checked type entry of its own.
        var returnStatementClone = returnStatement with { Value = returnClone };
        var rawClone = raw with { Body = [returnStatementClone] };
        var lifterInput = statements.Select(statement => ReferenceEquals(statement, raw) ? rawClone : statement).ToList();
        analysis.RecordRewrite(raw, rawClone);
        analysis.RecordRewrite(returnStatement, returnStatementClone);
        analysis.RecordRewrite(priorReturn, returnClone);
        var rewritten = NestedFunctionLifter.Lift(lifterInput, null, map, analysis.RecordRewrite);
        Assert.NotSame(lifterInput, rewritten);
        Assert.True(rewritten.OfType<Stmt.Function>().Count() > statements.OfType<Stmt.Function>().Count());
        analysis.Analyze(rewritten, new RuntimeFeatureDetector().Detect(rewritten, map));
        Assert.True(map.IsUndefinedReachableReturn(returnClone));
        Assert.Null(map.Get(returnClone));
        var emittedReturn = Assert.Single(Function(rewritten, "raw").Body!.OfType<Stmt.Return>()).Value!;
        Assert.True(map.IsUndefinedReachableReturn(emittedReturn));
        Assert.Null(map.Get(emittedReturn));
        var parameters = new Parameters();
        foreach (var statement in rewritten) parameters.Visit(statement);
        var clones = parameters.Items.Where(parameter =>
            ReferenceEquals(parameter.Name, sourceParameter.Name)).ToArray();
        Assert.True(clones.Length >= 2);
        Assert.Equal(clones.Length, clones.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.All(clones, parameter =>
        {
            Assert.NotSame(sourceParameter, parameter);
            Assert.True(map.IsUndefinedReachableNumericParam(parameter));
        });
        Assert.True(map.IsUndefinedReachableNumericParam(sourceParameter));
        var freshCapture = Assert.Single(parameters.Items, parameter =>
            parameter.Name.Lexeme == "seed" && !ReferenceEquals(parameter.Name, seed.Name));
        Assert.Null(freshCapture.Type);
        Assert.Null(map.GetValueBinding(freshCapture.Name));
    }

    private static void AssertModuleIsolation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "raw_storage_modules_" + Guid.NewGuid().ToString("N"));
        var entry = Path.Combine(directory, "main.ts");
        var left = Path.Combine(directory, "left.ts");
        var right = Path.Combine(directory, "right.ts");
        var source = RawCallStorageValueTests.BindingIsolationAndConversions;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [entry] = "import './left'; import './right';",
            [left] = "export {};\n" + source,
            [right] = "export {};\n" + source.Replace(
                "function raw(): number { return null as any; }", "function raw(): number { return 6; }")
        };
        var resolver = new ModuleResolver(entry, files, new TypeScriptProgramOptions { NoLib = true });
        var modules = resolver.GetModulesInOrder(resolver.LoadProgram(entry));
        var checker = new TypeChecker();
        var map = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Stmt.Var InnerRaw(string path) => Variable(Function(Assert.Single(modules, module =>
            string.Equals(module.Path, path, StringComparison.OrdinalIgnoreCase)).Statements, "outer").Body!
            .OfType<Stmt.Block>().Last().Statements, "value");
        var leftValue = InnerRaw(left);
        var rightValue = InnerRaw(right);
        var leftBinding = Assert.IsType<BindingSymbol>(map.GetValueBinding(leftValue.Name));
        var rightBinding = Assert.IsType<BindingSymbol>(map.GetValueBinding(rightValue.Name));
        Assert.NotSame(leftBinding, rightBinding);
        checker.Bindings.Clear();
        var all = modules.SelectMany(module => module.Statements).ToList();
        var analysis = RawCallStorageAnalysis.Capture(all, map, modules.Select(module => module.Statements));
        // Preparation mutates module lists in place after capture. Source partitions
        // must retain their original statements and binding identities.
        foreach (var module in modules) module.Statements.Clear();
        Assert.All(analysis.Modules, module => Assert.NotEmpty(module));
        analysis.Analyze(all, new RuntimeFeatureDetector().Detect(all, map));
        AssertLocal(map, leftValue, expectedRaw: true);
        AssertLocal(map, rightValue, expectedRaw: false);
        Assert.Same(leftBinding, map.GetValueBinding(leftValue.Name));
        Assert.Same(rightBinding, map.GetValueBinding(rightValue.Name));
    }

    private static void AssertLocal(TypeMap map, Stmt.Var declaration, bool expectedRaw)
    {
        Assert.Equal(expectedRaw, map.IsUndefinedReachableNumericLocal(declaration));
        Assert.Equal(expectedRaw, map.IsUndefinedReachableNumericLocal(declaration.Initializer!));
        Assert.Equal(expectedRaw, map.IsRawCallStorage(declaration.Name));
    }

    private static void AssertVirtualSlotFlags()
    {
        var statements = Parse(RawCallStorageValueTests.VirtualParameterSlots);
        var checker = new TypeChecker();
        var map = checker.Check(statements);
        var methods = statements.OfType<Stmt.Class>().ToDictionary(owner => owner.Name.Lexeme,
            owner => Assert.Single(owner.Methods, method => method.Name.Lexeme == "run"));
        CompileAndVerify(statements, map);
        foreach (var (owner, method) in methods)
        {
            var implementation = Assert.IsType<TypeInfo.Function>(map.GetClassMethodImplementation(method));
            Assert.Equal(owner.EndsWith("Child", StringComparison.Ordinal),
                map.IsClassMethodParameterUndefinedReachable(implementation, 0));
        }
    }

    private static List<Stmt> Parse(string source) => new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();

    private static Stmt.Function Function(IEnumerable<Stmt> statements, string name) =>
        Assert.Single(statements.OfType<Stmt.Function>(), statement => statement.Name.Lexeme == name);

    private static Stmt.Var Variable(IEnumerable<Stmt> statements, string name) =>
        Assert.Single(statements.OfType<Stmt.Var>(), statement => statement.Name.Lexeme == name);

    private static void CompileAndVerify(List<Stmt> statements, TypeMap map)
    {
        var compiler = new ILCompiler("raw_storage_analysis_" + Guid.NewGuid().ToString("N"));
        compiler.Compile(statements, map);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        using var stream = new MemoryStream(compiler.SaveToBytes());
        Assert.Empty(verifier.Verify(stream));
    }

    private sealed class Parameters : AstVisitorBase
    {
        public List<Stmt.Parameter> Items { get; } = [];

        protected override void VisitFunction(Stmt.Function function)
        {
            Items.AddRange(function.Parameters);
            base.VisitFunction(function);
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction arrow)
        {
            Items.AddRange(arrow.Parameters);
            base.VisitArrowFunction(arrow);
        }
    }
}
