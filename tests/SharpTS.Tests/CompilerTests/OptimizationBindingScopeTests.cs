using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class OptimizationBindingScopeTests
{
    public enum Consumer { NumericMap, MapIteration, PromiseThen, TypedArray }

    public static TheoryData<Consumer, string, bool> ScopeCases
    {
        get
        {
            var cases = new TheoryData<Consumer, string, bool>();
            foreach (var consumer in Enum.GetValues<Consumer>())
            {
                cases.Add(consumer, "", true);
                // These passes intentionally merge block declarations by function and name.
                cases.Add(consumer, "{ let value ANNOTATION = SEED; value = SEED; }", false);
                cases.Add(consumer, "function nested(): void { let value ANNOTATION = SEED; value = SEED; }", true);
                cases.Add(consumer, "const nested = (): void => { let value ANNOTATION = SEED; value = SEED; };", true);
                cases.Add(consumer, "const nested = () => value;", false);
                cases.Add(consumer, "function nested(): void {} value = SEED;", false);
                cases.Add(consumer, "const nested = (): void => {}; value = SEED;", false);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ScopeCases))]
    public void FunctionIdentity_BlockShadowingAndCaptures_KeepConservativeProof(
        Consumer consumer, string body, bool optimized)
    {
        AssertProof(consumer, body, optimized);
    }

    public static TheoryData<Consumer, string> WriteCases
    {
        get
        {
            var cases = new TheoryData<Consumer, string>();
            foreach (var consumer in Enum.GetValues<Consumer>())
            foreach (string write in new[]
            {
                "value = SEED;", "value ||= SEED;", "value &&= SEED;", "value ??= SEED;",
                "[value] = [SEED];", "({ item: value } = { item: SEED });",
                "({ item: [value] } = { item: [SEED] });"
            })
                cases.Add(consumer, write);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(WriteCases))]
    public void BindingWrites_DisqualifyEveryConsumer(Consumer consumer, string write) =>
        AssertProof(consumer, write, optimized: false);

    public static TheoryData<Consumer, string> UncheckedWriteCases
    {
        get
        {
            var cases = new TheoryData<Consumer, string>();
            foreach (var consumer in Enum.GetValues<Consumer>())
            foreach (string write in new[] { "value += 1;", "++value;", "--value;", "value++;", "value--;" })
                cases.Add(consumer, write);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(UncheckedWriteCases))]
    public void NonNumericBindingUpdates_AreConservativeEvenForUncheckedAst(Consumer consumer, string write) =>
        AssertProof(consumer, "", optimized: false, uncheckedWrite: write);

    [Theory]
    [InlineData("", true)]
    [InlineData("data[0]++;", true)]
    [InlineData("data = new Int32Array(1);", false)]
    [InlineData("data ||= new Int32Array(1);", false)]
    [InlineData("[data] = [new Int32Array(1)];", false)]
    [InlineData("{ const data = new Int32Array(1); }", false)]
    [InlineData("const nested = () => { data = new Int32Array(1); };", false)]
    public void LoopReceiverScan_RetainsNameBasedWriteAndDeclarationPolicy(string body, bool hoisted)
    {
        string source = $$"""
            function exercise(data: Int32Array): void {
                for (let i = 0; i < 1; i++) {
                    data[0] = 1;
                    {{body}}
                }
            }
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var function = Assert.IsType<Stmt.Function>(Assert.Single(statements));
        var loop = Assert.IsType<Stmt.For>(Assert.Single(function.Body!));
        var candidates = TypedArrayHoistAnalyzer.AnalyzeFor(loop.Body, loop.Condition, loop.Increment, typeMap);
        Assert.Equal(hoisted, candidates.ContainsKey("data"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("entry = replacement;", false)]
    [InlineData("entry ||= replacement;", false)]
    [InlineData("[entry] = [replacement];", false)]
    [InlineData("entry[0]++;", false)]
    [InlineData("++entry[1];", false)]
    [InlineData("{ const entry = [3, 4]; }", false)]
    public void MapEntryScan_RetainsBindingAndElementMutationPolicy(string body, bool optimized)
    {
        string source = $$"""
            function exercise(): void {
                const replacement: [number, number] = [3, 4];
                const map = new Map<number, number>();
                map.set(1, 2);
                for (let entry of map) {
                    {{body}}
                    console.log(entry[0] + entry[1]);
                }
            }
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var closures = new ClosureAnalyzer();
        closures.Analyze(statements);
        StableMapIterationAnalyzer.Analyze(statements, typeMap, closures);
        var proof = new ProofVisitor(typeMap);
        proof.Visit(Assert.Single(statements));
        Assert.Equal(optimized, proof.Optimized);
    }

    private static void AssertProof(Consumer consumer, string body, bool optimized, string? uncheckedWrite = null)
    {
        string seed = consumer switch
        {
            Consumer.NumericMap or Consumer.MapIteration => "new Map<number, number>()",
            Consumer.PromiseThen => "Promise.resolve(0)",
            Consumer.TypedArray => "new Int32Array(4)",
            _ => throw new ArgumentOutOfRangeException(nameof(consumer))
        };
        string use = consumer switch
        {
            Consumer.NumericMap => "value.set(1, 2); return value.size;",
            Consumer.MapIteration => "value.set(1, 2); for (const entry of value) { const sum = entry[0] + entry[1]; } return 0;",
            Consumer.PromiseThen => "value = value.then((n: number): number => n + 1); return value;",
            Consumer.TypedArray => "value[0] = 1; return value[0];",
            _ => throw new ArgumentOutOfRangeException(nameof(consumer))
        };
        string returnType = consumer == Consumer.PromiseThen ? "Promise<number>" : "number";
        string annotation = consumer == Consumer.PromiseThen ? ": Promise<number>" : "";
        string source = $$"""
            function exercise(): {{returnType}} {
                let value{{annotation}} = {{seed}};
                {{body.Replace("SEED", seed).Replace("ANNOTATION", annotation)}}
                {{use}}
            }
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        if (uncheckedWrite != null)
        {
            // Numeric updates on Map/Promise/TypedArray bindings are rejected by the checker.
            // Exercise the analyzer's write proof directly on an otherwise checked candidate.
            var function = Assert.IsType<Stmt.Function>(Assert.Single(statements));
            function.Body!.InsertRange(1, new Parser(new Lexer(uncheckedWrite).ScanTokens()).ParseOrThrow());
        }
        var closures = new ClosureAnalyzer();
        closures.Analyze(statements);
        switch (consumer)
        {
            case Consumer.NumericMap:
                NumericMapLocalPromotionAnalyzer.Analyze(statements, typeMap, closures);
                break;
            case Consumer.MapIteration:
                StableMapIterationAnalyzer.Analyze(statements, typeMap, closures);
                break;
            case Consumer.PromiseThen:
                StablePrimitivePromiseThenAnalyzer.Analyze(statements, typeMap, closures,
                    PromiseMutationAnalyzer.HasObservableMutation(statements, typeMap));
                break;
            case Consumer.TypedArray:
                TypedArrayHoistAnalyzer.Analyze(statements, typeMap, closures);
                break;
        }
        var proof = new ProofVisitor(typeMap);
        foreach (var statement in statements)
            proof.Visit(statement);
        Assert.Equal(optimized, proof.Optimized);
    }

    private sealed class ProofVisitor(TypeMap typeMap) : AstVisitorBase
    {
        public bool Optimized { get; private set; }

        protected override void VisitFunction(Stmt.Function statement)
        {
            if (statement.Name.Lexeme == "exercise")
                base.VisitFunction(statement);
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }

        protected override void VisitVar(Stmt.Var statement)
        {
            Optimized |= typeMap.IsPromotableNumericMapLocal(statement.Name);
            base.VisitVar(statement);
        }

        protected override void VisitVariable(Expr.Variable expression) =>
            Optimized |= typeMap.IsStableTypedArrayBackingReceiver(expression);

        protected override void VisitGet(Expr.Get expression)
        {
            Optimized |= typeMap.IsStablePrimitivePromiseThen(expression);
            base.VisitGet(expression);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            Optimized |= typeMap.IsStableNumericMapIteration(statement);
            base.VisitForOf(statement);
        }
    }
}
