using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class MaterializedRecordReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string source in new[]
        {
            "const box:{value?:number}={value:1};box.value=undefined;console.log(box.value===undefined,typeof box.value,box.value);",
            "const box:{value?:number}={value:1};box['value']=undefined;console.log(box.value===undefined,typeof box.value,box.value);",
            "function clear(box:{value?:number}|{value?:string}){box.value=undefined;}const box={value:1};clear(box);console.log(box.value===undefined,typeof box.value,box.value);",
            "const box:{value?:number}=JSON.parse('{\"value\":1}');box.value=undefined;console.log(box.value===undefined,typeof box.value,box.value);",
        }) yield return [source];
    }

    public static IEnumerable<object[]> ParityCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode => new object[] { row[0], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void GeneralReadsPreserveTheActualUndefinedValueAfterMaterialization(string source, ExecutionMode mode)
        => Assert.Equal("true undefined undefined\n", TestHarness.Run(source, mode));

    [Theory, MemberData(nameof(Cases))]
    public void RawMaterializedReadProgramsHaveVerifiedIl(string source)
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(source));

    [Fact]
    public void UnchangedNumericRecordsKeepTheirCompactProofAndNativeStorage()
    {
        const string source = "const box={value:2};console.log(box.value*3);";
        var statements = TestHarness.ParseOrThrow(source);
        var types = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, types);
        var shape = Assert.Single(features.CompactObjectRecordShapes);
        Assert.True(features.CanAssumeCompactObjectRecordIsUnmaterialized(shape.Key));
        var compiler = new ILCompiler("record_read_proof_" + Guid.NewGuid().ToString("N"));
        compiler.Compile(statements, types, new DeadCodeAnalyzer(types).Analyze(statements));
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var carrier = Assert.Single(assembly.GetTypes(), type => type.Name.StartsWith("$CompactObjectRecord", StringComparison.Ordinal));
        Assert.Contains(carrier.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            field => field.FieldType == typeof(double));
        Assert.Equal("6\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }
}
