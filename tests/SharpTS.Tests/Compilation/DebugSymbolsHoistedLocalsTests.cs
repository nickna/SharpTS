using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Compilation.Symbols;
using SharpTS.Testing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.Compilation;

public partial class DebugSymbolsTests
{
    private const string TimerDelaySource = "const delay = () => new Promise<number>((resolve): void => { setTimeout(() => resolve(0), 1); });\n";

    [Theory]
    [InlineData("async")]
    [InlineData("generator")]
    [InlineData("async-generator")]
    public void HoistedBindingsHaveSourceNamesAndScopesAcrossSuspensions(string kind)
    {
        string source = StateMachineSource(kind, """
            let carried = seed + 1;
            console.log(carried); // before-first
            SUSPEND
            carried += 1;
            console.log(carried); // after-first
            SUSPEND
            console.log(carried); // after-second
            return carried;
            """);
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        AssertStateMachineIdentity(artifacts, machine);
        IReadOnlyList<HoistedFieldScope> fields = ReadHoistedFieldScopes(artifacts, machine);
        HoistedFieldScope parameter = Assert.Single(fields, field => field.SourceName == "seed");
        HoistedFieldScope local = Assert.Single(fields, field => field.SourceName == "carried");
        foreach (string marker in new[] { "before-first", "after-first", "after-second" })
        {
            int offset = SourceMarkerOffset(artifacts, machine, source, marker);
            Assert.True(parameter.Scope.Contains(offset), "The source parameter survives each suspension.");
            Assert.True(local.Scope.Contains(offset), "The source local survives each suspension.");
        }
        Assert.DoesNotContain(AllLocals(artifacts), local => local.Name.Contains(">5__", StringComparison.Ordinal));
        AssertCodeViewMatchesPdb(artifacts.Assembly, artifacts.Pdb!, "output.pdb");
    }

    [Theory]
    [InlineData("async")]
    [InlineData("generator")]
    [InlineData("async-generator")]
    public void ShadowedHoistedBindingsKeepDistinctStorageAndLexicalRanges(string kind)
    {
        string source = StateMachineSource(kind, """
            let value = seed;
            console.log(value); // outer-before
            {
                let seed = 99;
                let value = seed + 10;
                SUSPEND
                console.log(value, seed); // inner-first
            }
            {
                let value = seed + 20;
                SUSPEND
                console.log(value); // inner-second
            }
            console.log(value); // outer-after
            return value;
            """);
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        HoistedFieldScope[] shadows = ReadHoistedFieldScopes(artifacts, machine)
            .Where(field => field.SourceName == "value").ToArray();
        Assert.Equal(3, shadows.Length);
        Assert.Equal(3, shadows.Select(field => field.Slot).Distinct().Count());
        int before = SourceMarkerOffset(artifacts, machine, source, "outer-before");
        int after = SourceMarkerOffset(artifacts, machine, source, "outer-after");
        HoistedFieldScope outer = Assert.Single(shadows, field => field.Scope.Contains(before));
        Assert.True(outer.Scope.Contains(after));
        foreach (string marker in new[] { "inner-first", "inner-second" })
        {
            int inside = SourceMarkerOffset(artifacts, machine, source, marker);
            HoistedFieldScope inner = Assert.Single(shadows,
                field => field.Field != outer.Field && field.Scope.Contains(inside));
            Assert.False(inner.Scope.Contains(before));
            Assert.False(inner.Scope.Contains(after));
            Assert.True(inner.Scope.Length < outer.Scope.Length);
        }
        HoistedFieldScope[] parameterShadows = ReadHoistedFieldScopes(artifacts, machine)
            .Where(field => field.SourceName == "seed").ToArray();
        Assert.Equal(2, parameterShadows.Length);
        HoistedFieldScope parameter = Assert.Single(parameterShadows, field => field.Scope.Contains(after));
        HoistedFieldScope parameterShadow = Assert.Single(parameterShadows, field => field.Field != parameter.Field);
        Assert.True(parameterShadow.Scope.Contains(SourceMarkerOffset(artifacts, machine, source, "inner-first")));
        Assert.False(parameterShadow.Scope.Contains(after));
    }

    [Theory]
    [InlineData("async", "for")]
    [InlineData("generator", "for")]
    [InlineData("async-generator", "for")]
    [InlineData("async", "for-of")]
    [InlineData("generator", "for-of")]
    [InlineData("async-generator", "for-of")]
    [InlineData("async", "for-in")]
    [InlineData("generator", "for-in")]
    [InlineData("async-generator", "for-in")]
    public void HoistedLoopAndCatchBindingsExpireAtTheirSourceScope(string kind, string loopKind)
    {
        string loopHeader = loopKind switch
        {
            "for" => "for (let index = 0; index < seed; index++)",
            "for-of" => "for (const index of [seed])",
            "for-in" => "for (const index in { a: seed })",
            _ => throw new ArgumentOutOfRangeException(nameof(loopKind)),
        };
        string source = StateMachineSource(kind, """
            let total = 0;
            LOOP {
                let item = seed + 1;
                SUSPEND
                total += item; // loop-body
                console.log(index);
            }
            console.log(total); // after-loop
            let error = seed + 100;
            try {
                throw seed;
            } catch (error) {
                SUSPEND
                console.log(error); // catch-body
            }
            console.log(error, total); // after-catch
            return total;
            """.Replace("LOOP", loopHeader, StringComparison.Ordinal));
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        IReadOnlyList<HoistedFieldScope> fields = ReadHoistedFieldScopes(artifacts, machine);
        int loop = SourceMarkerOffset(artifacts, machine, source, "loop-body");
        int afterLoop = SourceMarkerOffset(artifacts, machine, source, "after-loop");
        foreach (string name in new[] { "index", "item" })
        {
            HoistedFieldScope binding = Assert.Single(fields, field => field.SourceName == name);
            Assert.True(binding.Scope.Contains(loop));
            Assert.False(binding.Scope.Contains(afterLoop));
        }
        int afterCatch = SourceMarkerOffset(artifacts, machine, source, "after-catch");
        HoistedFieldScope[] errors = fields.Where(field => field.SourceName == "error").ToArray();
        Assert.Equal(2, errors.Length);
        HoistedFieldScope outerError = Assert.Single(errors, field => field.Scope.Contains(afterCatch));
        HoistedFieldScope caught = Assert.Single(errors, field => field.Field != outerError.Field);
        Assert.True(caught.Scope.Contains(SourceMarkerOffset(artifacts, machine, source, "catch-body")));
        Assert.False(caught.Scope.Contains(afterCatch));
    }

    [Theory]
    [InlineData("async")]
    [InlineData("generator")]
    [InlineData("async-generator")]
    public void DestructuringAndVarLoweringKeepUserBindingsWithoutProjectingSpills(string kind)
    {
        string source = StateMachineSource(kind, """
            const [first, second] = [seed, seed + 1];
            const __user = first;
            const _destUser = second;
            if (seed > 0) { var carried = first + second; }
            SUSPEND
            console.log(first, second, __user, _destUser, carried); // after-lowering
            return first;
            """);
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        IReadOnlyList<HoistedFieldScope> fields = ReadHoistedFieldScopes(artifacts, machine);
        string[] userNames = ["seed", "first", "second", "__user", "_destUser", "carried"];
        Assert.Equal(userNames.Order(StringComparer.Ordinal), fields.Select(field => field.SourceName).Order(StringComparer.Ordinal));
        int offset = SourceMarkerOffset(artifacts, machine, source, "after-lowering");
        Assert.All(fields, field => Assert.True(field.Scope.Contains(offset)));
        Assert.All(AllLocals(artifacts).Where(local => local.Name.StartsWith("_dest", StringComparison.Ordinal)),
            local => Assert.True(local.Hidden));
    }

    [Theory]
    [InlineData("async")]
    [InlineData("generator")]
    [InlineData("async-generator")]
    public void CapturedBindingsProjectTheirSharedStorageInsteadOfStateMachineCopies(string kind)
    {
        string source = StateMachineSource(kind, """
            let carried = seed;
            const increment = () => ++carried;
            SUSPEND
            console.log(increment(), carried); // shared-capture
            return carried;
            """);
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        string[] stateFields = metadata.GetTypeDefinition(machine.StateMachineType).GetFields()
            .Select(field => metadata.GetString(metadata.GetFieldDefinition(field).Name)).ToArray();
        Assert.Contains(stateFields, name => name.StartsWith("<>8__", StringComparison.Ordinal));
        IReadOnlyList<HoistedFieldScope> fields = ReadHoistedFieldScopes(artifacts, machine);
        HoistedFieldScope captured = Assert.Single(fields, field => field.SourceName == "carried" && field.Scope.Length > 0);
        Assert.NotEqual(machine.StateMachineType, captured.DeclaringType);
        Assert.True(captured.Scope.Contains(SourceMarkerOffset(artifacts, machine, source, "shared-capture")));
        Assert.All(fields.Where(field => field.SourceName == "carried" && field.DeclaringType == machine.StateMachineType),
            field => Assert.Equal(0, field.Scope.Length));
    }

    [Theory]
    [InlineData("async")]
    [InlineData("generator")]
    [InlineData("async-generator")]
    public void CapturedShadowUsesAuthoritativeDisplayClassFieldOnlyWithinItsLexicalScope(string kind)
    {
        string source = StateMachineSource(kind, """
            let value = seed;
            {
                let value = seed + 10;
                const mutate = () => { value += 1; return value; };
                SUSPEND
                console.log(mutate(), value); // inner-capture
            }
            console.log(value); // outside-capture
            return value;
            """);
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        IReadOnlyList<HoistedFieldScope> fields = ReadHoistedFieldScopes(artifacts, machine);
        int inside = SourceMarkerOffset(artifacts, machine, source, "inner-capture");
        int outside = SourceMarkerOffset(artifacts, machine, source, "outside-capture");
        HoistedFieldScope captured = Assert.Single(fields, field => field.SourceName == "value" &&
            field.DeclaringType != machine.StateMachineType && field.Scope.Contains(inside) && !field.Scope.Contains(outside));
        Assert.False(captured.Scope.Contains(outside));
        HoistedFieldScope outer = Assert.Single(fields, field => field.SourceName == "value" && field.Scope.Contains(outside));
        Assert.NotEqual(captured.Field, outer.Field);
    }

    [Theory]
    [InlineData("async")]
    [InlineData("generator")]
    [InlineData("async-generator")]
    public void CapturedOuterBindingSurvivesAnUncapturedInnerShadow(string kind)
    {
        string source = StateMachineSource(kind, """
            let value = seed;
            const mutate = () => ++value;
            {
                let value = seed + 10;
                SUSPEND
                console.log(value); // inner-shadow
            }
            SUSPEND
            console.log(mutate(), value); // captured-outer
            return value;
            """);
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        IReadOnlyList<HoistedFieldScope> fields = ReadHoistedFieldScopes(artifacts, machine);
        int after = SourceMarkerOffset(artifacts, machine, source, "captured-outer");
        HoistedFieldScope outer = Assert.Single(fields, field => field.SourceName == "value" && field.Scope.Contains(after));
        Assert.NotEqual(machine.StateMachineType, outer.DeclaringType);
        int inside = SourceMarkerOffset(artifacts, machine, source, "inner-shadow");
        HoistedFieldScope inner = Assert.Single(fields, field => field.SourceName == "value" &&
            field.Field != outer.Field && field.Scope.Contains(inside));
        Assert.False(inner.Scope.Contains(after));
    }

    [Fact]
    public void AsyncArrowsAndClassMethodsUseTheirActualNestedKickoffIdentity()
    {
        string source = TimerDelaySource + """
            class Processor {
                async work(seed: number) { const carried = seed; await delay(); return carried; }
                *values(seed: number) { const carried = seed; yield carried; return carried; }
                async *stream(seed: number) { const carried = seed; await delay(); yield carried; }
            }
            const arrow = async (seed: number) => { const carried = seed; await delay(); return carried; };
            const worker = new Processor();
            worker.work(1); worker.values(2); worker.stream(3); arrow(4);
            """;
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        IReadOnlyList<HoistedStateMachineScopes> machines = ReadAndValidateHoistedScopes(artifacts);
        Assert.Equal(4, machines.Count);
        foreach (HoistedStateMachineScopes machine in machines)
        {
            AssertStateMachineIdentity(artifacts, machine);
            IReadOnlyList<HoistedFieldScope> fields = ReadHoistedFieldScopes(artifacts, machine);
            Assert.Contains(fields, field => field.SourceName == "seed");
            Assert.Contains(fields, field => field.SourceName == "carried");
        }
    }

    [Fact]
    public void CapturedAsyncArrowIncludesOwnBindingsAndOuterCapturesAcrossSuspension()
    {
        string source = CapturedAsyncArrowSource();
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        MetadataReader pdb = ReadPdb(artifacts.Pdb!);
        int line = SourceMarkerLine(source, "captured-arrow-before");
        HoistedStateMachineScopes arrow = Assert.Single(ReadAndValidateHoistedScopes(artifacts),
            machine => SequencePoints(pdb, MetadataTokens.GetRowNumber(machine.MoveNext))
                .Any(point => !point.IsHidden && point.StartLine == line));
        AssertStateMachineIdentity(artifacts, arrow);
        IReadOnlyList<HoistedFieldScope> fields = ReadHoistedFieldScopes(artifacts, arrow);
        int before = SourceMarkerOffset(artifacts, arrow, source, "captured-arrow-before");
        int after = SourceMarkerOffset(artifacts, arrow, source, "captured-arrow-after");
        foreach (string name in new[] { "parameter", "local", "seed", "snapshot", "shared" })
        {
            HoistedFieldScope field = Assert.Single(fields,
                field => field.SourceName == name && field.Scope.Contains(before));
            Assert.True(field.Scope.Contains(after), $"The arrow's {name} binding remains available after its await.");
            Assert.Single(fields, field => field.SourceName == name && field.Scope.Contains(after));
        }
        HoistedFieldScope shared = Assert.Single(fields,
            field => field.SourceName == "shared" && field.Scope.Contains(after));
        Assert.NotEqual(arrow.StateMachineType, shared.DeclaringType);
    }

    [Fact]
    public void CapturedAsyncArrowRetainsOuterValuesAndSharedMutationInBothBuildModes()
    {
        string source = CapturedAsyncArrowSource();
        const string expected = "before 45 46 47 91\nafter 45 46 48 91\nowner 48\nresult 91\n";
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    private static string CapturedAsyncArrowSource() => TimerDelaySource + """
        async function capturedArrowOwner(seed: number): Promise<number> {
            const snapshot = seed + 1;
            let shared = seed + 2;
            const arrow = async (parameter: number): Promise<number> => {
                const local = parameter + snapshot;
                console.log("before", seed, snapshot, shared, local); // captured-arrow-before
                await delay();
                shared++;
                console.log("after", seed, snapshot, shared, local); // captured-arrow-after
                return local;
            };
            const result = await arrow(seed);
            console.log("owner", shared);
            return result;
        }
        capturedArrowOwner(45).then((result) => console.log("result", result));
        """;

    [Fact]
    public void NamespacedAndStaticStateMachinesKeepTheirKickoffIdentity()
    {
        string source = TimerDelaySource + """
            namespace N {
                export async function work(seed: number) { const carried = seed; await delay(); return carried; }
                export function* values(seed: number) { const carried = seed; yield carried; return carried; }
                export async function* stream(seed: number) { const carried = seed; await delay(); yield carried; }
            }
            class Service {
                static async work(seed: number) { const carried = seed; await delay(); return carried; }
            }
            N.work(1); N.values(2); N.stream(3); Service.work(4);
            """;
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        IReadOnlyList<HoistedStateMachineScopes> machines = ReadAndValidateHoistedScopes(artifacts);
        Assert.Equal(4, machines.Count);
        Assert.All(machines, machine => AssertStateMachineIdentity(artifacts, machine));
        Assert.All(machines, machine => Assert.Contains(ReadHoistedFieldScopes(artifacts, machine),
            field => field.SourceName == "carried" && field.Scope.Length > 0));
    }

    [Fact]
    public void ImportedStateMachinesKeepModuleDocumentAndKickoffAssociation()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sharpts_hoisted_modules_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string helper = Path.Combine(directory, "helper.ts");
            File.WriteAllText(helper, TimerDelaySource + "export async function work(seed: number) { const carried = seed; await delay(); return carried; }");
            string entry = Path.Combine(directory, "main.ts");
            File.WriteAllText(entry, "import { work } from './helper'; work(1);");
            CompilationArtifacts artifacts = CompileModules(entry, "main");
            HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
            AssertStateMachineIdentity(artifacts, machine);
            MetadataReader pdb = ReadPdb(artifacts.Pdb!);
            MethodDebugInformation information = pdb.GetMethodDebugInformation(
                MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(machine.MoveNext)));
            SequencePoint point = information.GetSequencePoints().First(point => !point.IsHidden);
            DocumentHandle document = information.Document.IsNil ? point.Document : information.Document;
            Assert.Equal(helper, pdb.GetString(pdb.GetDocument(document).Name));
            AssertCodeViewMatchesPdb(artifacts.Assembly, artifacts.Pdb!, "main.pdb");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void HoistedDebugNamesAndScopeRecordsAreDeterministic()
    {
        string source = StateMachineSource("async", """
            let value = seed;
            { let value = seed + 1; SUSPEND console.log(value); }
            SUSPEND
            return value;
            """);
        CompilationArtifacts first = CompileTypeScript(source, emitDebugSymbols: true);
        CompilationArtifacts second = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes firstMachine = Assert.Single(ReadAndValidateHoistedScopes(first));
        HoistedStateMachineScopes secondMachine = Assert.Single(ReadAndValidateHoistedScopes(second));
        Assert.Equal(firstMachine.Scopes, secondMachine.Scopes);
        Assert.Equal(ReadHoistedFieldScopes(first, firstMachine), ReadHoistedFieldScopes(second, secondMachine));
        Assert.Equal(GeneratedTypeNames(first.Assembly), GeneratedTypeNames(second.Assembly));
    }

    [Theory]
    [InlineData("async")]
    [InlineData("generator")]
    [InlineData("async-generator")]
    public void NonDebugStateMachinesRetainTheirOrdinaryLayoutAndFieldNames(string kind)
    {
        string source = StateMachineSource(kind, "let carried = seed; SUSPEND return carried;");
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: false);
        Assert.Null(artifacts.Pdb);
        Assert.Empty(ReadDebugDirectory(artifacts.Assembly));
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        TypeDefinition machine = Assert.Single(metadata.TypeDefinitions.Select(metadata.GetTypeDefinition),
            type => metadata.GetString(type.Name).Contains("<work>d__", StringComparison.Ordinal));
        Assert.True(machine.GetDeclaringType().IsNil);
        string[] names = machine.GetFields().Select(field => metadata.GetString(metadata.GetFieldDefinition(field).Name)).ToArray();
        Assert.Contains("seed", names);
        Assert.Contains("carried", names);
        Assert.DoesNotContain(names, IsProjectedUserFieldName);
    }

    [Fact]
    public void NamedLocalSlotsBelongToTheirFinalMethodSignatures()
    {
        string source = TimerDelaySource + """
            function ordinary(seed: number) { const local = seed + 1; return local; }
            async function work(seed: number) { const local = seed + 1; await delay(); return local; }
            function* values(seed: number) { const local = seed + 1; yield local; return local; }
            async function* stream(seed: number) { const local = seed + 1; await delay(); yield local; }
            ordinary(1); work(2); values(3); stream(4);
            """;
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        AssertNamedLocalSlotsAreValid(artifacts);
        Func<int, int> slotCount = PdbEmitter.ReadLocalSlotCounts(artifacts.Assembly);
        Assert.Equal(0, slotCount(0));
        Assert.Equal(0, slotCount(int.MaxValue));
    }

    [Theory]
    [InlineData("async")]
    [InlineData("generator")]
    [InlineData("async-generator")]
    public void SuspendedCatchParametersKeepThrownValueAndRestoreOuterBindingInBothBuildModes(string kind)
    {
        string source = StateMachineRuntimeSource(kind, """
            let error = 99;
            try {
                SUSPEND
                throw 7;
            } catch (error) {
                SUSPEND
                console.log("caught", error);
            }
            console.log("outer", error);
            return seed;
            """);
        Assert.Equal("caught 7\nouter 99\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal("caught 7\nouter 99\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    [Theory]
    [InlineData("async", "for-of", "let")]
    [InlineData("async", "for-of", "var")]
    [InlineData("async", "for-of", "assignment")]
    [InlineData("async", "for-in", "let")]
    [InlineData("async", "for-in", "var")]
    [InlineData("async", "for-in", "assignment")]
    [InlineData("generator", "for-of", "let")]
    [InlineData("generator", "for-of", "var")]
    [InlineData("generator", "for-of", "assignment")]
    [InlineData("generator", "for-in", "let")]
    [InlineData("generator", "for-in", "var")]
    [InlineData("generator", "for-in", "assignment")]
    [InlineData("async-generator", "for-of", "let")]
    [InlineData("async-generator", "for-of", "var")]
    [InlineData("async-generator", "for-of", "assignment")]
    [InlineData("async-generator", "for-in", "let")]
    [InlineData("async-generator", "for-in", "var")]
    [InlineData("async-generator", "for-in", "assignment")]
    public void SuspendedIterationBindingsHaveTheSameRuntimeBehaviorInBothBuildModes(string kind, string loopKind, string bindingKind)
    {
        string binding = bindingKind == "assignment" ? "item" : bindingKind + " item";
        string initializer = bindingKind switch
        {
            "assignment" => "let item: any = 0;",
            "var" => "var item: any = 0;",
            _ => "",
        };
        string header = loopKind == "for-of"
            ? $"for ({binding} of [3, 4])"
            : $"for ({binding} in {{ a: 1 }})";
        string after = bindingKind == "let" ? "console.log('done');" : "console.log('after', item);";
        string source = StateMachineRuntimeSource(kind,
            initializer + "\n" + header + " {\n SUSPEND\n console.log(item);\n}\n" + after);
        string values = loopKind == "for-of" ? "3\n4\n" : "a\n";
        string expected = values + (bindingKind == "let" ? "done\n" : "after " + (loopKind == "for-of" ? "4\n" : "a\n"));
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: true));
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: false));
    }

    private static string StateMachineRuntimeSource(string kind, string body)
    {
        (string declaration, string suspension, string launch) = kind switch
        {
            "async" => ("async function work(seed: number)", "await delay();", "work(2);"),
            "generator" => ("function* work(seed: number)", "yield seed;", "for (const ignored of work(2)) {}"),
            "async-generator" => ("async function* work(seed: number)", "await delay(); yield seed;",
                "async function consume() { for await (const ignored of work(2)) {} } consume();"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return TimerDelaySource +
            declaration + " {\n" + body.Replace("SUSPEND", suspension, StringComparison.Ordinal) + "\n}\n" + launch;
    }

    private static string CompileAndRunDebugArtifact(string source, bool emitDebugSymbols, bool verifyIl = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sharpts_hoisted_runtime_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols);
            string assemblyPath = Path.Combine(directory, "output.dll");
            File.WriteAllBytes(assemblyPath, artifacts.Assembly);
            if (artifacts.Pdb is not null)
                File.WriteAllBytes(Path.Combine(directory, "output.pdb"), artifacts.Pdb);
            if (verifyIl)
                Assert.DoesNotContain(TestHarness.VerifyIL(assemblyPath),
                    error => !error.Contains("Failed to load assembly", StringComparison.Ordinal));

            // Match the existing compiled test host while keeping each build in an isolated process.
            string runtime = typeof(ILCompiler).Assembly.Location;
            File.Copy(runtime, Path.Combine(directory, "SharpTS.dll"));
            string compression = Path.Combine(Path.GetDirectoryName(runtime)!, "ZstdSharp.dll");
            if (File.Exists(compression)) File.Copy(compression, Path.Combine(directory, "ZstdSharp.dll"));
            File.WriteAllText(Path.Combine(directory, "output.runtimeconfig.json"), """
                { "runtimeOptions": { "tfm": "net10.0", "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" } } }
                """);
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory };
            start.ArgumentList.Add(assemblyPath);
            TestProcessResult result = TestProcess.Run(start, TestHarness.DefaultTimeout);
            Assert.True(result.ExitCode == 0, result.StandardError);
            Assert.Equal(emitDebugSymbols, File.Exists(Path.Combine(directory, "output.pdb")));
            return result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertNamedLocalSlotsAreValid(CompilationArtifacts artifacts)
    {
        Func<int, int> slotCount = PdbEmitter.ReadLocalSlotCounts(artifacts.Assembly);
        MetadataReader pdb = ReadPdb(artifacts.Pdb!);
        foreach (LocalScopeHandle handle in pdb.LocalScopes)
        {
            LocalScope scope = pdb.GetLocalScope(handle);
            int count = slotCount(MetadataTokens.GetRowNumber(scope.Method));
            foreach (LocalVariableHandle variableHandle in scope.GetLocalVariables())
            {
                LocalVariable variable = pdb.GetLocalVariable(variableHandle);
                Assert.InRange(variable.Index, 0, count - 1);
            }
        }
    }

    private static string StateMachineSource(string kind, string body)
    {
        (string declaration, string suspension, string launch) = kind switch
        {
            "async" => ("async function work(seed: number)", "await delay();", "work(2);"),
            "generator" => ("function* work(seed: number)", "yield seed;", "const it = work(2); it.next(); it.next(); it.next();"),
            "async-generator" => ("async function* work(seed: number)", "await delay(); yield seed;", "work(2);"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return TimerDelaySource + declaration + " {\n" + body.Replace("SUSPEND", suspension, StringComparison.Ordinal) + "\n}\n" + launch;
    }

    private static int SourceMarkerOffset(
        CompilationArtifacts artifacts, HoistedStateMachineScopes machine, string source, string marker)
    {
        int line = SourceMarkerLine(source, marker);
        MetadataReader pdb = ReadPdb(artifacts.Pdb!);
        SequencePoint point = Assert.Single(SequencePoints(pdb, MetadataTokens.GetRowNumber(machine.MoveNext)),
            point => !point.IsHidden && point.StartLine == line);
        return point.Offset;
    }

    private static int SourceMarkerLine(string source, string marker) =>
        Assert.Single(source.Split('\n').Select((text, index) => (text, line: index + 1)),
            item => item.text.Contains(marker, StringComparison.Ordinal)).line;

    private static void AssertStateMachineIdentity(CompilationArtifacts artifacts, HoistedStateMachineScopes machine)
    {
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        TypeDefinition stateType = metadata.GetTypeDefinition(machine.StateMachineType);
        MethodDefinition kickoff = metadata.GetMethodDefinition(machine.Kickoff);
        Assert.Equal(kickoff.GetDeclaringType(), stateType.GetDeclaringType());
        Assert.StartsWith("<" + metadata.GetString(kickoff.Name) + ">d__",
            metadata.GetString(stateType.Name).Replace('-', '.'));
    }

    private sealed record HoistedFieldScope(
        FieldDefinitionHandle Field, TypeDefinitionHandle DeclaringType, string SourceName, int Slot, HoistedLocalScope Scope);

    private static bool IsProjectedUserFieldName(string name)
    {
        int separator = name.IndexOf('>');
        return name.StartsWith('<') && separator > 1 && name.AsSpan(separator).StartsWith(">5__", StringComparison.Ordinal);
    }

    private static IReadOnlyList<HoistedFieldScope> ReadHoistedFieldScopes(
        CompilationArtifacts artifacts, HoistedStateMachineScopes machine)
    {
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        var fields = new List<HoistedFieldScope>();
        var pending = new Queue<TypeDefinitionHandle>();
        var visited = new HashSet<TypeDefinitionHandle>();
        var referencedSlots = new HashSet<int>();
        pending.Enqueue(machine.StateMachineType);
        while (pending.TryDequeue(out TypeDefinitionHandle type))
        {
            if (!visited.Add(type)) continue;
            foreach (FieldDefinitionHandle handle in metadata.GetTypeDefinition(type).GetFields())
            {
                FieldDefinition field = metadata.GetFieldDefinition(handle);
                string name = metadata.GetString(field.Name);
                if (name.StartsWith("<>8__", StringComparison.Ordinal))
                {
                    Assert.True(int.TryParse(name.AsSpan(5), out int holderSuffix), $"Invalid display-class slot suffix: {name}");
                    int holderSlot = holderSuffix - 1;
                    Assert.InRange(holderSlot, 0, machine.Scopes.Count - 1);
                    Assert.True(machine.Scopes[holderSlot].Length > 0, "A projected closure holder must have a live scope.");
                    referencedSlots.Add(holderSlot);
                    BlobReader signature = metadata.GetBlobReader(field.Signature);
                    Assert.Equal(SignatureKind.Field, signature.ReadSignatureHeader().Kind);
                    Assert.Equal(SignatureTypeCode.TypeHandle, signature.ReadSignatureTypeCode());
                    EntityHandle target = signature.ReadTypeHandle();
                    Assert.Equal(HandleKind.TypeDefinition, target.Kind);
                    Assert.Equal(0, signature.RemainingBytes);
                    var displayClass = (TypeDefinitionHandle)target;
                    Assert.Contains("DisplayClass", metadata.GetString(metadata.GetTypeDefinition(displayClass).Name));
                    pending.Enqueue(displayClass);
                    continue;
                }
                int separator = name.IndexOf('>');
                if (!IsProjectedUserFieldName(name))
                    continue;
                Assert.True(int.TryParse(name.AsSpan(separator + 4), out int suffix), $"Invalid user-field slot suffix: {name}");
                int slot = suffix - 1;
                Assert.InRange(slot, 0, machine.Scopes.Count - 1);
                referencedSlots.Add(slot);
                fields.Add(new HoistedFieldScope(handle, type, name[1..separator], slot, machine.Scopes[slot]));
            }
        }
        HoistedFieldScope[] active = fields.Where(field => field.Scope.Length > 0).ToArray();
        Assert.Equal(active.Length, active.Select(field => field.Slot).Distinct().Count());
        Assert.All(machine.Scopes.Where(scope => scope.Length > 0), scope =>
            Assert.Contains(scope.Slot, referencedSlots));
        return fields;
    }

    internal readonly record struct HoistedLocalScope(int Slot, int StartOffset, int Length)
    {
        internal int EndOffset => checked(StartOffset + Length);
        internal bool Contains(int offset) => StartOffset <= offset && offset < EndOffset;
    }

    internal sealed record HoistedStateMachineScopes(
        MethodDefinitionHandle MoveNext,
        MethodDefinitionHandle Kickoff,
        TypeDefinitionHandle StateMachineType,
        IReadOnlyList<HoistedLocalScope> Scopes);

    /// <summary>
    /// Decodes the complete hoisted-local scope records against the final rewritten assembly.
    /// The blob index is a hoisted field slot, not a CLR local-signature slot. Unused slots may
    /// have the Roslyn sentinel (0, 0), but every live scope must describe real IL instructions.
    /// </summary>
    internal static IReadOnlyList<HoistedStateMachineScopes> ReadAndValidateHoistedScopes(
        CompilationArtifacts artifacts)
    {
        AssertNamedLocalSlotsAreValid(artifacts);
        Guid hoistedScopesKind = new("6da9a61e-f8c7-4874-be62-68bc5630df71");
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        MetadataReader pdb = ReadPdb(artifacts.Pdb!);
        var records = new List<HoistedStateMachineScopes>();
        var owners = new HashSet<MethodDefinitionHandle>();

        foreach (CustomDebugInformationHandle handle in pdb.CustomDebugInformation)
        {
            CustomDebugInformation information = pdb.GetCustomDebugInformation(handle);
            if (pdb.GetGuid(information.Kind) != hoistedScopesKind)
                continue;

            Assert.Equal(HandleKind.MethodDefinition, information.Parent.Kind);
            var moveNext = (MethodDefinitionHandle)information.Parent;
            Assert.True(owners.Add(moveNext), "A method must have only one hoisted-scope record.");
            int methodRid = MetadataTokens.GetRowNumber(moveNext);
            Assert.InRange(methodRid, 1, metadata.MethodDefinitions.Count);

            MethodDefinition method = metadata.GetMethodDefinition(moveNext);
            Assert.Contains(metadata.GetString(method.Name), new[] { "MoveNext", "MoveNextAsync" });
            MethodDefinitionHandle kickoff = pdb.GetMethodDebugInformation(
                MetadataTokens.MethodDebugInformationHandle(methodRid)).GetStateMachineKickoffMethod();
            Assert.False(kickoff.IsNil, "Hoisted scopes must belong to a mapped state-machine body.");
            Assert.InRange(MetadataTokens.GetRowNumber(kickoff), 1, metadata.MethodDefinitions.Count);
            Assert.NotEqual(moveNext, kickoff);
            Assert.NotEqual(0, method.RelativeVirtualAddress);

            byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            HashSet<int> boundaries = ReadInstructionBoundaries(il);
            BlobReader blob = pdb.GetBlobReader(information.Value);
            Assert.Equal(0, blob.Length % 8);
            var scopes = new List<HoistedLocalScope>();
            while (blob.RemainingBytes != 0)
            {
                uint start = blob.ReadUInt32();
                uint length = blob.ReadUInt32();
                Assert.InRange(start, 0u, (uint)int.MaxValue);
                Assert.InRange(length, 0u, (uint)int.MaxValue);
                var scope = new HoistedLocalScope(scopes.Count, (int)start, (int)length);
                scopes.Add(scope);
                if (length == 0)
                {
                    Assert.Equal(0u, start);
                    continue;
                }

                Assert.InRange((long)start + length, 1L, (long)il.Length);
                Assert.True(boundaries.Contains(scope.StartOffset), "Scope starts inside an IL instruction.");
                Assert.True(boundaries.Contains(scope.EndOffset), "Scope ends inside an IL instruction.");
            }
            Assert.Equal(0, blob.RemainingBytes);
            records.Add(new HoistedStateMachineScopes(moveNext, kickoff, method.GetDeclaringType(), scopes));
        }

        return records;
    }

    /// <summary>
    /// Reads instruction boundaries rather than treating arbitrary offsets within a method as
    /// valid scope endpoints. This catches a structurally sized but unusable portable-PDB blob.
    /// </summary>
    internal static HashSet<int> ReadInstructionBoundaries(ReadOnlySpan<byte> il)
    {
        Dictionary<short, OpCode> opcodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opcode => opcode.Value);
        var boundaries = new HashSet<int>();
        int offset = 0;
        while (offset < il.Length)
        {
            boundaries.Add(offset);
            short value = il[offset++];
            if (value == 0xfe)
            {
                Assert.True(offset < il.Length, "Truncated two-byte IL opcode.");
                value = unchecked((short)(0xfe00 | il[offset++]));
            }
            Assert.True(opcodes.TryGetValue(value, out OpCode opcode), "Invalid IL opcode.");
            int operandLength = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or
                    OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or
                    OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => ReadSwitchOperandLength(il[offset..]),
                _ => throw new InvalidOperationException($"Unsupported IL operand: {opcode.OperandType}"),
            };
            Assert.True(operandLength <= il.Length - offset, "Truncated IL operand.");
            offset += operandLength;
        }
        Assert.Equal(il.Length, offset);
        boundaries.Add(offset);
        return boundaries;
    }

    private static int ReadSwitchOperandLength(ReadOnlySpan<byte> operand)
    {
        Assert.True(operand.Length >= sizeof(int), "Truncated IL switch operand.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(operand);
        Assert.InRange(count, 0, (operand.Length - sizeof(int)) / sizeof(int));
        return sizeof(int) + count * sizeof(int);
    }
}
