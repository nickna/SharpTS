using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;
using TI = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Tests.CompilerTests;

public sealed class SymbolUnionParameterSlotTests
{
    [Theory]
    [InlineData("no-narrowing", typeof(object))]
    [InlineData("string-literal-union-control", typeof(string))]
    public void PlannedParameterSlotsKeepSymbolsAndClosedStringOptimization(string name, Type expectedSlot)
    {
        var source = (string)SymbolUnionParameterTests.Cases().Single(row => (string)row[0] == name)[1];
        var statements = TestHarness.ParseOrThrow(source);
        var types = new TypeChecker(TypeCheckerOptions.Strict).Check(statements);
        var function = Assert.Single(statements.OfType<Stmt.Function>());
        var functionType = Assert.IsType<TI.Function>(types.GetFunctionType(function.Name.Lexeme));
        var module = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("symbol_parameter_slots_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("main");
        var mapper = new TypeMapper(module);
        mapper.SetUnionGenerator(new UnionTypeGenerator(mapper));

        var slots = ParameterTypeResolver.ResolveParameters(function.Parameters, mapper, functionType, types);
        Assert.Same(expectedSlot, Assert.Single(slots));
    }
}
