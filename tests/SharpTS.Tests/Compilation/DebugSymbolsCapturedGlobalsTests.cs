using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace SharpTS.Tests.Compilation;

public partial class DebugSymbolsTests
{
    [Fact]
    public void StaticGlobalCaptureSnapshotsAreHiddenAndRuntimeReadsStayLive()
    {
        string source = """
            let release: (value: number) => void = (value: number): void => {};
            const pending = new Promise<number>((resolve): void => { release = resolve; });
            let shared = 1;
            const action = async () => {
                const own = 17;
                console.log("before", shared);
                await pending;
                console.log("after", shared); // global-after
                return own;
            };
            shared = 2;
            const launched = action();
            shared = 3;
            release(0);
            launched.then((): void => {});
            """;
        var artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        Assert.DoesNotContain(ReadHoistedFieldScopes(artifacts, machine), field => field.SourceName == "shared");
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        string[] names = metadata.GetTypeDefinition(machine.StateMachineType).GetFields()
            .Select(handle => metadata.GetString(metadata.GetFieldDefinition(handle).Name)).ToArray();
        Assert.Contains("<>7__captured_shared", names);
        Assert.Equal("before 2\nafter 3\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal("before 2\nafter 3\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    [Fact]
    public void CapturedLocalSharingAGlobalNameKeepsAuthoritativeDisplayClassStorage()
    {
        string source = TimerDelaySource + """
            let shared = 100;
            function owner() {
                let shared = 1;
                const action = async () => {
                    console.log("before", shared);
                    await delay();
                    console.log("after", shared); // local-after
                };
                shared = 2;
                action();
                shared = 3;
            }
            owner();
            console.log("global", shared);
            """;
        var artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        var holders = metadata.GetTypeDefinition(machine.StateMachineType).GetFields()
            .Select(metadata.GetFieldDefinition)
            .Where(field => metadata.GetString(field.Name).StartsWith("<>8__", StringComparison.Ordinal)).ToArray();
        Assert.Contains(holders, holder =>
        {
            BlobReader signature = metadata.GetBlobReader(holder.Signature);
            Assert.Equal(SignatureKind.Field, signature.ReadSignatureHeader().Kind);
            Assert.Equal(SignatureTypeCode.TypeHandle, signature.ReadSignatureTypeCode());
            var displayClass = (TypeDefinitionHandle)signature.ReadTypeHandle();
            return metadata.GetTypeDefinition(displayClass).GetFields().Any(handle =>
                metadata.GetString(metadata.GetFieldDefinition(handle).Name) == "shared");
        });
        Assert.DoesNotContain(metadata.GetTypeDefinition(machine.StateMachineType).GetFields(), handle =>
            metadata.GetString(metadata.GetFieldDefinition(handle).Name) == "<>7__captured_shared");
        Assert.Equal("before 2\nglobal 100\nafter 3\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal("before 2\nglobal 100\nafter 3\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    [Fact]
    public void ReadOnlySyncClosureObservesOwnerMutationOfAShadowAcrossSuspensionInBothModes()
    {
        string source = TimerDelaySource + """
            async function work(parameter: number) {
                let local = parameter + 1;
                {
                    let local = 98;
                    const shadowReader = () => local;
                    local++;
                    await delay();
                    console.log("captured shadow", shadowReader());
                }
                console.log("restored", local);
            }
            work(10);
            """;
        Assert.Equal("captured shadow 99\nrestored 11\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal("captured shadow 99\nrestored 11\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadOnlyNumericLoopShadowKeepsPerIterationCapturesAndOuterBinding(bool emitDebugSymbols)
    {
        string source = """
            async function work(limit: number): Promise<void> {
                let item: number = 7;
                await Promise.resolve(0);
                let chain: Promise<number> = Promise.resolve(0);
                {
                    for (let item: number = 0; item < limit; item++) {
                        chain = chain.then((sum: number): number => sum + item);
                    }
                }
                const sum = await chain;
                console.log("result", sum, item);
            }
            work(3);
            """;
        Assert.Equal("result 3 7\n", CompileAndRunDebugArtifact(source, emitDebugSymbols, verifyIl: true));
    }

    [Theory]
    [InlineData(true, "const", "for")]
    [InlineData(false, "const", "for")]
    [InlineData(true, "let", "for")]
    [InlineData(false, "let", "for")]
    [InlineData(true, "const", "while")]
    [InlineData(false, "const", "while")]
    [InlineData(true, "let", "do-while")]
    [InlineData(false, "let", "do-while")]
    public void ReadOnlyLoopBodyShadowKeepsIndependentIterationValues(
        bool emitDebugSymbols, string declarationKind, string loopKind)
    {
        string body = """
            DECLARATION item = cursor;
            chain = chain.then((sum: number): number => sum + item);
            """.Replace("DECLARATION", declarationKind, StringComparison.Ordinal);
        string loop = loopKind switch
        {
            "for" => "for (let cursor = 0; cursor < 3; cursor++) {\n" + body + "\n}",
            "while" => "let cursor = 0; while (cursor < 3) {\n" + body + "\ncursor++;\n}",
            _ => "let cursor = 0; do {\n" + body + "\ncursor++;\n} while (cursor < 3);"
        };
        string source = """
            async function work(): Promise<void> {
                let item = 7;
                await Promise.resolve(0);
                let chain: Promise<number> = Promise.resolve(0);
                LOOP
                const sum = await chain;
                console.log("result", sum, item);
            }
            work();
            """.Replace("LOOP", loop, StringComparison.Ordinal);
        Assert.Equal("result 3 7\n", CompileAndRunDebugArtifact(source, emitDebugSymbols, verifyIl: true));
    }

    [Fact]
    public void IndependentStateMachinesKeepHoistedSlotTablesBoundedPerCallable()
    {
        string source = TimerDelaySource + string.Join('\n', Enumerable.Range(0, 40).Select(index =>
            $"async function work{index}(seed: number) {{ const carried = seed + 1; await delay(); return carried; }}"));
        var artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        IReadOnlyList<HoistedStateMachineScopes> machines = ReadAndValidateHoistedScopes(artifacts);
        Assert.Equal(40, machines.Count);
        Assert.All(machines, machine =>
        {
            Assert.Equal(4, machine.Scopes.Count);
            Assert.Equal(2, ReadHoistedFieldScopes(artifacts, machine).Count);
        });
    }

    [Fact]
    public void TransitiveAsyncArrowCapturesImportAncestorAndParentBindingsAcrossSuspension()
    {
        string source = TransitiveAsyncArrowCaptureSource();
        var artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        MetadataReader pdb = ReadPdb(artifacts.Pdb!);
        int line = SourceMarkerLine(source, "grandchild-before");
        HoistedStateMachineScopes grandchild = Assert.Single(ReadAndValidateHoistedScopes(artifacts),
            machine => SequencePoints(pdb, MetadataTokens.GetRowNumber(machine.MoveNext))
                .Any(point => !point.IsHidden && point.StartLine == line));
        AssertStateMachineIdentity(artifacts, grandchild);
        IReadOnlyList<HoistedFieldScope> fields = ReadHoistedFieldScopes(artifacts, grandchild);
        int before = SourceMarkerOffset(artifacts, grandchild, source, "grandchild-before");
        int after = SourceMarkerOffset(artifacts, grandchild, source, "grandchild-after");
        var visible = new List<HoistedFieldScope>();
        foreach (string name in new[] { "seed", "shared", "parentLocal", "childParameter", "childLocal" })
        {
            HoistedFieldScope field = Assert.Single(fields,
                field => field.SourceName == name && field.Scope.Contains(before));
            Assert.True(field.Scope.Contains(after), $"The transitive {name} binding survives the grandchild's await.");
            Assert.Single(fields, field => field.SourceName == name && field.Scope.Contains(after));
            visible.Add(field);
        }
        Assert.Equal(visible.Count, visible.Select(field => field.Slot).Distinct().Count());
        Assert.All(visible.Where(field => field.SourceName is "seed" or "shared"),
            field => Assert.NotEqual(grandchild.StateMachineType, field.DeclaringType));
        Assert.All(visible.Where(field => field.SourceName is "childParameter" or "childLocal"),
            field => Assert.Equal(grandchild.StateMachineType, field.DeclaringType));
    }

    [Fact]
    public void TransitiveAsyncArrowCapturesRetainLiveValuesInBothBuildModes()
    {
        string source = TransitiveAsyncArrowCaptureSource();
        const string expected = "before 10 12 13 13 26\nafter 10 12 13 13 26\nparent 13 26\nowner 12 26\nresult 26\n";
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    private static string TransitiveAsyncArrowCaptureSource() => TimerDelaySource + """
        async function owner(seed: number): Promise<number> {
            let shared = seed + 1;
            const parent = async (parentParameter: number): Promise<number> => {
                const parentLocal = parentParameter + 1;
                const child = async (childParameter: number): Promise<number> => {
                    const childLocal = childParameter + parentLocal;
                    console.log("before", seed, shared, parentLocal, childParameter, childLocal); // grandchild-before
                    await delay();
                    console.log("after", seed, shared, parentLocal, childParameter, childLocal); // grandchild-after
                    return childLocal;
                };
                await delay();
                const result = await child(parentLocal);
                console.log("parent", parentLocal, result);
                return result;
            };
            const pending = parent(seed + 2);
            shared++;
            const result = await pending;
            console.log("owner", shared, result);
            return result;
        }
        owner(10).then((result) => console.log("result", result));
        """;
}
