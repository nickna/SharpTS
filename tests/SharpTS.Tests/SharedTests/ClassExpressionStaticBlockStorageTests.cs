using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassExpressionStaticBlockStorageTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["anonymous-static-block-var-capture-issue", """
            let result = "";
            const C = class {
                static {
                    const read = () => value;
                    var value = "block-var";
                    result = read();
                }
            };
            console.log(result);
            """ + "\n", "block-var\n"];
        yield return ["static-block-live-var-and-lexical-owners", """
            var value = 99;
            var lexicalLet = 90;
            var lexicalConst = 'outer';
            const reads: any[] = [];
            const Anonymous = class {
                static {
                    const readAnonymous = () => value;
                    const readAnonymousThis = () => this;
                    console.log('anonymous-before', readAnonymous() === undefined, readAnonymousThis() === this);
                    var value = 'first';
                    console.log('anonymous-after', readAnonymous());
                    const readAfterInit = () => value;
                    value = 'first-live';
                    console.log('anonymous-live', readAnonymous(), readAfterInit());
                    var value = 'first-redeclared';
                    console.log('anonymous-redeclared', readAnonymous(), readAfterInit());
                    reads.push(readAnonymous);
                }
                static {
                    const readSibling = () => value;
                    console.log('sibling-before', readSibling() === undefined);
                    var value = 'second';
                    console.log('sibling-after', readSibling());
                    value = 'second-live';
                    reads.push(readSibling);
                }
            };
            console.log('anonymous-retained', reads[0](), reads[1]());
            const Named = class Owner {
                static {
                    const readNamed = () => value;
                    const readNamedThis = () => this;
                    const readOwner = () => Owner;
                    console.log('named-before', readNamed() === undefined, readNamedThis() === this, readOwner() === this);
                    var value = 'named';
                    console.log('named-after', readNamed());
                    value = 'named-live';
                    reads.push(readNamed);
                }
                static {
                    const readLet = () => lexicalLet;
                    try { readLet(); } catch (error: any) { console.log('let-before', error instanceof ReferenceError); }
                    let lexicalLet = 31;
                    console.log('let-after', readLet());
                    lexicalLet = 32;
                    console.log('let-live', readLet());
                }
                static {
                    const readConst = () => lexicalConst;
                    try { readConst(); } catch (error: any) { console.log('const-before', error instanceof ReferenceError); }
                    const lexicalConst = 'constant';
                    console.log('const-after', readConst());
                }
            };
            console.log('named-retained', reads[2]());
            console.log('outer', value, lexicalLet, lexicalConst);
            """ + "\n", "anonymous-before true true\nanonymous-after first\nanonymous-live first-live first-live\nanonymous-redeclared first-redeclared first-redeclared\nsibling-before true\nsibling-after second\nanonymous-retained first-redeclared second-live\nnamed-before true true true\nnamed-after named\nlet-before true\nlet-after 31\nlet-live 32\nconst-before true\nconst-after constant\nnamed-retained named-live\nouter 99 90 outer\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void StaticBlockClosuresKeepTheirOwnLiveVarAndLexicalBindings(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
