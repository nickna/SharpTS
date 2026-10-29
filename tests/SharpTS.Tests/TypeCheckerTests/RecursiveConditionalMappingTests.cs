using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class RecursiveConditionalMappingTests
{
    private const string Definitions = """
        type ConvertedObject<T extends object> = { [K in keyof T]: Converted<T[K]> };
        type Converted<T> = T extends string|number|boolean|null ? T
            : T extends undefined|Function ? never
            : T extends {toJSON():infer R} ? R
            : T extends object ? ConvertedObject<T> : "unknown";
        declare class Custom { toJSON():"correct"; }
        declare class Stamp { toJSON():string; }
        """;

    [Fact]
    public void ConcreteMappedConditionalMembersMaterializeTheirSelectedBranches()
    {
        string source = Definitions + """
            type Input={custom:Custom,stamp:Stamp,fn:()=>void,literal:"fixed",obj:{nested:{attr:Stamp}}};
            declare let ex:Converted<Input>;
            const custom=ex.custom; const date=ex.stamp; const removed=ex.fn;
            const literal=ex.literal; const nested=ex.obj.nested.attr;
            const shape=ex; const object=ex.obj; const nestedObject=ex.obj.nested;
            type Expected={custom:"correct",stamp:string,fn:never,literal:"fixed",obj:{nested:{attr:string}}};
            declare const expected:Expected;
            const forward:Expected=ex; const reverse:Converted<Input>=expected;
            """;
        var document = new SourceDocument("recursive-mapping.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(strictNullChecks:true).WithEditorMetadata();
        TypeMap map = checker.Check(statements, document);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        TypeInfo Inferred(string name) => Assert.IsAssignableFrom<TypeInfo>(map.Get(
            statements.OfType<Stmt.Const>().Single(statement => statement.Name.Lexeme == name).Initializer!));
        Assert.Equal("correct", Assert.IsType<TypeInfo.StringLiteral>(Inferred("custom")).Value);
        Assert.IsType<TypeInfo.String>(Inferred("date"));
        Assert.IsType<TypeInfo.Never>(Inferred("removed"));
        Assert.Equal("fixed", Assert.IsType<TypeInfo.StringLiteral>(Inferred("literal")).Value);
        Assert.IsType<TypeInfo.String>(Inferred("nested"));
        var shape = Assert.IsType<TypeInfo.Record>(Inferred("shape"));
        Assert.Equal(new[] { "custom", "fn", "literal", "obj", "stamp" }, shape.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.IsType<TypeInfo.Record>(Inferred("object"));
        Assert.IsType<TypeInfo.String>(Assert.IsType<TypeInfo.Record>(Inferred("nestedObject")).Fields["attr"]);
        AssertMaterialized(shape);
        Assert.Contains(checker.EditorFacts.GetSourceTypeObservations(document), fact =>
            source[fact.Span.Start..fact.Span.End] == "ex.custom" && fact.Display.Text == "\"correct\"");

        static void AssertMaterialized(TypeInfo type)
        {
            Assert.False(type is TypeInfo.Any or TypeInfo.ConditionalType or TypeInfo.InferredTypeParameter
                or TypeInfo.TypeParameter or TypeInfo.RecursiveTypeAlias);
            if (type is TypeInfo.Record record)
                foreach (var member in record.Fields.Values) AssertMaterialized(member);
        }
    }

    [Fact]
    public void RecursiveObjectEdgesStayLazyAndDoNotPreventConcreteLeafInference()
    {
        string source = Definitions + """
            type Tree={value:Custom,next?:Tree};
            declare let tree:Converted<Tree>;
            const first=tree.value; const second=tree.next!.value;
            const check:"correct"=second;
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(strictNullChecks:true);
        var map = checker.Check(statements);
        var second = statements.OfType<Stmt.Const>().Single(statement => statement.Name.Lexeme == "second");
        Assert.Equal("correct", Assert.IsType<TypeInfo.StringLiteral>(map.Get(second.Initializer!)).Value);
    }

    [Fact]
    public void HomomorphicReadonlyAndOptionalMembersSurviveConditionalMaterialization()
    {
        string source = Definitions + """
            type Input={readonly custom:Custom,optional?:Custom};
            declare let value:Converted<Input>;
            const optional=value.optional;
            value.custom="correct";
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var result = new TypeChecker(strictNullChecks:true).CheckWithRecovery(statements);
        Assert.Equal("TS2540", Assert.Single(result.Diagnostics).TsCode);
        var declaration = statements.OfType<Stmt.Const>().Single(statement => statement.Name.Lexeme == "optional");
        var optional = Assert.IsType<TypeInfo.Union>(result.TypeMap.Get(declaration.Initializer!));
        Assert.True(optional.ContainsUndefined);
        Assert.Contains(optional.FlattenedTypes, type => type is TypeInfo.StringLiteral { Value: "correct" });
    }

    [Fact]
    public void MaterializationUsesTheCurrentModulesAliasAndClassDeclarations()
    {
        var modules = new[] { "first", "second" }.Select(value =>
        {
            string source = Definitions.Replace("\"correct\"", $"\"{value}\"", StringComparison.Ordinal) + """
                declare const input:Converted<{value:Custom}>;
                const observed=input.value; export {};
                """;
            return new ParsedModule(Path.Combine(Path.GetTempPath(), value+".ts"),
                new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        }).ToList();
        var checker = new TypeChecker(strictNullChecks:true);
        var map = checker.CheckModules(modules, new ModuleResolver(Path.GetTempPath()));
        Assert.Empty(checker.GetDiagnostics());
        for (int index = 0; index < modules.Count; index++)
        {
            var observed = modules[index].Statements.OfType<Stmt.Const>().Single(statement => statement.Name.Lexeme == "observed");
            Assert.Equal(index == 0 ? "first" : "second", Assert.IsType<TypeInfo.StringLiteral>(map.Get(observed.Initializer!)).Value);
        }
    }
}
