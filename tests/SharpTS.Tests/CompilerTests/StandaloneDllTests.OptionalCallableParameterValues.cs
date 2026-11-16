using SharpTS.Tests.Infrastructure;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(OptionalCallableParameterValueTests.Cases), MemberType = typeof(OptionalCallableParameterValueTests))]
    public void Isolated_Issue2408OptionalCallableParameters_VerifyBeforeExecution(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("optional-callable-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        if (name == "bcl-delegate-control")
            AssertOptionalCallableNativeStringPredicate(output);
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    private static void AssertOptionalCallableNativeStringPredicate(string output)
    {
        using var stream = File.OpenRead(output);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        bool IsSystemType(TypeReferenceHandle handle, string name)
        {
            var type = metadata.GetTypeReference(handle);
            return metadata.GetString(type.Namespace) == "System" && metadata.GetString(type.Name) == name;
        }

        var existsMembers = metadata.MemberReferences.Where(handle =>
        {
            var member = metadata.GetMemberReference(handle);
            return metadata.GetString(member.Name) == "Exists" && member.Parent.Kind == HandleKind.TypeReference
                && IsSystemType((TypeReferenceHandle)member.Parent, "Array");
        }).ToHashSet();
        Assert.NotEmpty(existsMembers);
        Assert.Contains(metadata.TypeReferences, handle => IsSystemType(handle, "Predicate`1"));
        Assert.Contains(Enumerable.Range(1, metadata.GetTableRowCount(TableIndex.MethodSpec)), row =>
        {
            var specification = metadata.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row));
            if (specification.Method.Kind != HandleKind.MemberReference
                || !existsMembers.Contains((MemberReferenceHandle)specification.Method)) return false;
            // GENERICINST, one argument, ELEMENT_TYPE_STRING: Array.Exists<string>
            // therefore consumes the real native Predicate<string> adapter.
            var signature = metadata.GetBlobBytes(specification.Signature);
            return signature.Length == 3 && signature[0] == 0x0a && signature[1] == 0x01 && signature[2] == 0x0e;
        });
    }
}
