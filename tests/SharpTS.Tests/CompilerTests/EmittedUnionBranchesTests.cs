using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;
using TI = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedUnionBranchesTests
{
    [Fact]
    public void FiveMemberUnionStringificationVerifiesAndReturnsEachActiveValue()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("union_branches_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("main");
        var contract = module.DefineType("IUnionValue", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var getter = contract.DefineMethod("get_Value", MethodAttributes.Public | MethodAttributes.Abstract
            | MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig, typeof(object), Type.EmptyTypes);
        var property = contract.DefineProperty("Value", PropertyAttributes.None, typeof(object), null);
        property.SetGetMethod(getter);
        var generator = new UnionTypeGenerator(new TypeMapper(module), contract.CreateType());
        var union = generator.GetOrCreateUnionType(new TI.Union([new TI.Any(), new TI.Null(), TI.String.Shared,
            new TI.StringLiteral("left"), new TI.StringLiteral("right")]), module);
        generator.FinalizeAllUnionTypes();
        using var bytes = new MemoryStream();
        builder.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier();
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "SharpTS");
        var type = assembly.GetType(union.FullName!)!;
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        for (byte index = 0; index < 5; index++)
        {
            var value = Activator.CreateInstance(type)!;
            type.GetField("_tag", fields)!.SetValue(value, index);
            type.GetField("_v" + index, fields)!.SetValue(value, "slot" + index);
            Assert.Equal("slot" + index, value.ToString());
        }
    }
}
