using SharpTS.Tests.Infrastructure;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(AbstractClassPrototypeTests.Cases), MemberType = typeof(AbstractClassPrototypeTests))]
    public void Isolated_Issue2428AbstractClassPrototypes_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("abstract-prototypes-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        if (name == "inheritance-descriptors-and-initialization")
            AssertAbstractClassPrototypeTypeMetadata(output);
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    private static void AssertAbstractClassPrototypeTypeMetadata(string output)
    {
        using var stream = File.OpenRead(output);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var (name, isAbstract) in new[] { ("Parent", true), ("Middle", true), ("Child", false) })
        {
            var handle = Assert.Single(metadata.TypeDefinitions, candidate =>
            {
                var emittedName = metadata.GetString(metadata.GetTypeDefinition(candidate).Name);
                return emittedName == name || emittedName.EndsWith("_" + name, StringComparison.Ordinal);
            });
            var type = metadata.GetTypeDefinition(handle);
            Assert.Equal(isAbstract, (type.Attributes & TypeAttributes.Abstract) != 0);
            var bookkeepingConstructor = Assert.Single(type.GetMethods().Select(metadata.GetMethodDefinition),
                method =>
                {
                    if (metadata.GetString(method.Name) != ".ctor"
                        || (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.FamORAssem)
                        return false;
                    var signature = metadata.GetBlobReader(method.Signature);
                    var header = signature.ReadSignatureHeader();
                    return header.Kind == SignatureKind.Method && !header.IsGeneric
                        && signature.ReadCompressedInteger() == 1;
                });
            Assert.NotEqual(0, bookkeepingConstructor.RelativeVirtualAddress);
        }
    }
}
