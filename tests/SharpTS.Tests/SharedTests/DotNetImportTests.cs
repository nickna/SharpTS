using SharpTS.Declaration;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for <c>dotnet:</c> import-scheme resolution (epic #1195): first-class imports of
/// .NET types with reflection-synthesized static types. Success scenarios run in BOTH
/// execution modes — the interpreter binds <c>DotNetClass</c> wrappers, compiled mode emits
/// direct external-interop IL — and must produce identical output.
/// </summary>
public class DotNetImportTests
{
    /// <summary>
    /// Type-checks a module set and returns error diagnostics. Statement-level type errors in
    /// module mode are COLLECTED (recovery), not thrown — the CLI prints them and refuses to
    /// run; tests must assert on the collected list (mirrors Program.cs RunModuleFile).
    /// </summary>
    private static List<Diagnostic> CheckErrors(Dictionary<string, string> files, string entryPoint)
    {
        var virtualBase = Path.Combine(Path.GetTempPath(), $"sharpts_dnimp_{Guid.NewGuid():N}");
        var virtualFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, content) in files)
            virtualFiles[Path.GetFullPath(Path.Combine(virtualBase, path.TrimStart('.', '/', '\\')))] = content;
        var entryPath = Path.GetFullPath(Path.Combine(virtualBase, entryPoint.TrimStart('.', '/', '\\')));

        var resolver = new ModuleResolver(entryPath, virtualFiles);
        var entryModule = resolver.LoadModule(entryPath);
        var allModules = resolver.GetModulesInOrder(entryModule);

        var checker = new TypeChecker();
        checker.CheckModules(allModules, resolver);
        return checker.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }

    #region Single-type form

    [Theory, ModeData]
    public void ObjectValuedHostAwaitables_AreRealPromisesInBothModes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { HostAsyncInteropFixture } from "dotnet:SharpTS.Tests.Infrastructure.HostAsyncInteropFixture";
                const fixture = new HostAsyncInteropFixture();
                const readValues = async (): Promise<string[]> =>
                    JSON.parse(await fixture.jsonStringListAsync());
                const printNested = async (value: string): Promise<void> => {
                    console.log("nested:" + value);
                };
                const runWrapped = async (): Promise<void> => {
                    try {
                        const confirmed = (await fixture.pendingStringAsync()) === "pending";
                        if (!confirmed) return;
                        const wrappedValues = await readValues();
                        console.log(wrappedValues.join("|"));
                        await printNested(wrappedValues[0]);
                        console.log("after-nested");
                    } catch (error) {
                        console.log("unexpected-wrapped-error");
                    }
                };
                async function main(): Promise<void> {
                    console.log(await fixture.completedStringAsync());
                    console.log(await fixture.pendingStringAsync());
                    console.log(await fixture.nullStringAsync());
                    const values = JSON.parse(await fixture.jsonStringListAsync());
                    console.log(values.join(","));
                    await runWrapped();
                    await fixture.completedVoidAsync();
                    await fixture.completedStringAsync().then(value => console.log("then:" + value));
                    try {
                        await fixture.faultedStringAsync();
                    } catch (error) {
                        console.log(String(error).includes("managed-async-failure"));
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal(
            "completed\npending\nnull\nalpha,beta\nalpha|beta\nnested:alpha\nafter-nested\nthen:completed\ntrue\n",
            output);
    }

    [Theory, ModeData]
    public void SingleTypeForm_ConstructChainAndProperty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                const sb = new StringBuilder();
                sb.append("Hello").append(", ").append("dotnet:").append(42);
                console.log(sb.toString());
                console.log(sb.length);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Hello, dotnet:42\n16\n", output);
    }

    [Fact]
    public void SingleTypeForm_FluentChainIsStaticallyTyped()
    {
        // append() returns the synthesized StringBuilder type (not any) — assigning the
        // chain result to number must be a static type error.
        var errors = CheckErrors(new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                const sb = new StringBuilder();
                const n: number = sb.append("x");
                """
        }, "./main.ts");

        Assert.Contains(errors, d => d.Message.Contains("not assignable"));
    }

    [Fact]
    public void SingleTypeForm_PrimitiveReturnTypesArePrecise()
    {
        // toString(): string — assigning to number is a static error.
        var errors = CheckErrors(new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                const s: number = new StringBuilder().toString();
                """
        }, "./main.ts");

        Assert.Contains(errors, d => d.Message.Contains("not assignable"));
    }

    [Theory, ModeData]
    public void ConstructedGenericList_ConstructionMethodsAndIndexer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { List } from "dotnet:System.Collections.Generic.List<number>";
                const values = new List();
                values.add(1.5);
                values.add(2.5);
                values[1] = 9.25;
                console.log(values[0]);
                console.log(values[1]);
                console.log(values.count);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1.5\n9.25\n2\n", output);
    }

    [Theory, ModeData]
    public void ConstructedGenericDictionary_StringIndexer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Dictionary } from "dotnet:System.Collections.Generic.Dictionary<string, number>";
                const values = new Dictionary();
                values["answer"] = 42;
                console.log(values["answer"]);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void ConstructedGenericIndexer_IsStaticallyTyped()
    {
        var errors = CheckErrors(new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { List } from "dotnet:System.Collections.Generic.List<number>";
                const values = new List();
                values[0] = "not a number";
                """
        }, "./main.ts");

        Assert.Contains(errors, d => d.Message.Contains("index signature", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConstructedGenericResolver_HandlesNestedArguments()
    {
        var type = Runtime.DotNet.DotNetTypeRegistry.ResolveFriendly(
            "System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<number>>");

        Assert.Equal(typeof(Dictionary<string, List<double>>), type);
    }

    [Theory, ModeData]
    public void NullableValueTypes_RoundTripValuesAndNull(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { NullableFixture } from "dotnet:SharpTS.Tests.Infrastructure.NullableFixture";
                const fixture = new NullableFixture();
                const present: number | null = fixture.echo(7);
                const missing: number | null = fixture.echo(null);
                console.log(present);
                console.log(missing);
                console.log(fixture.orDefault(missing, 11));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("7\nnull\n11\n", output);
    }

    [Fact]
    public void NullableValueReturn_IsStaticallyNullable()
    {
        var errors = CheckErrors(new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { NullableFixture } from "dotnet:SharpTS.Tests.Infrastructure.NullableFixture";
                const fixture = new NullableFixture();
                const value: number = fixture.echo(null);
                """
        }, "./main.ts");

        Assert.Contains(errors, d => d.Message.Contains("not assignable", StringComparison.OrdinalIgnoreCase));
    }

    [Theory, ModeData]
    public void ByRefParameters_AreTupleLowered(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { ByRefFixture } from "dotnet:SharpTS.Tests.Infrastructure.ByRefFixture";
                const fixture = new ByRefFixture();
                const [ok, parsed] = fixture.tryDouble("41");
                const [incremented] = fixture.increment(parsed);
                const [message, mixed, changed] = fixture.mix(1, incremented);
                console.log(ok);
                console.log(parsed);
                console.log(incremented);
                console.log(message);
                console.log(mixed);
                console.log(changed);
                console.log(fixture.readOnlyAdd(mixed, 2));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n41\n42\nvalue=43\n43\ntrue\n45\n", output);
    }

    [Fact]
    public void ByRefTuple_IsStaticallyTypedAndOutIsNotAnInput()
    {
        var errors = CheckErrors(new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { ByRefFixture } from "dotnet:SharpTS.Tests.Infrastructure.ByRefFixture";
                const fixture = new ByRefFixture();
                const result: [boolean, number] = fixture.tryDouble("12");
                const wrong: string = result[1];
                fixture.tryDouble("12", 0);
                """
        }, "./main.ts");

        Assert.Contains(errors, d => d.Message.Contains("not assignable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, d => d.Message.Contains("Expected 1 arguments", StringComparison.OrdinalIgnoreCase));
    }

    [Theory, ModeData]
    public void BclTryParse_UsesTupleLowering(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Guid } from "dotnet:System.Guid";
                const [ok, value] = Guid.tryParse("d85b1407-351d-4694-9392-03acc5870eb1");
                const typed: Guid = value;
                console.log(ok);
                console.log(typed.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\nd85b1407-351d-4694-9392-03acc5870eb1\n", output);
    }

    [Theory, ModeData]
    public void UserDefinedClrOperators_DispatchForImportedOperands(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { OperatorFixture } from "dotnet:SharpTS.Tests.Infrastructure.OperatorFixture";
                const a = new OperatorFixture(4);
                const b = new OperatorFixture(6);
                const sum: OperatorFixture = a + b;
                const scaled: OperatorFixture = sum * 3;
                console.log(sum.value);
                console.log(scaled.value);
                console.log(b > a);
                console.log(a < b);
                console.log(a === new OperatorFixture(4));
                console.log(a !== b);
                console.log((-a).value);
                console.log(!new OperatorFixture(0));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("10\n30\ntrue\ntrue\ntrue\ntrue\n-4\ntrue\n", output);
    }

    [Theory, ModeData]
    public void UserDefinedClrOperators_SupportCompoundAndIncrementVariables(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { OperatorFixture } from "dotnet:SharpTS.Tests.Infrastructure.OperatorFixture";
                let value = new OperatorFixture(2);
                const added: OperatorFixture = value += new OperatorFixture(3);
                console.log(added.value);
                console.log(value.value);
                const scaled: OperatorFixture = value *= 2;
                console.log(scaled.value);
                const old: OperatorFixture = value++;
                console.log(old.value);
                console.log(value.value);
                const decremented: OperatorFixture = --value;
                console.log(decremented.value);
                console.log(value.value);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("5\n5\n10\n10\n11\n10\n10\n", output);
    }

    [Theory, ModeData]
    public void UserDefinedClrOperators_WriteBackPropertiesAndIndexersOnce(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { OperatorFixture } from "dotnet:SharpTS.Tests.Infrastructure.OperatorFixture";
                const holder = new OperatorFixture(4);
                let receiverCalls = 0;
                let indexCalls = 0;
                function receiver(): OperatorFixture {
                    receiverCalls++;
                    return holder;
                }
                function index(): number {
                    indexCalls++;
                    return 0;
                }

                const propertyResult: OperatorFixture =
                    receiver().current += new OperatorFixture(2);
                console.log(propertyResult.value);

                const oldIndex: OperatorFixture = receiver()[index()]++;
                console.log(oldIndex.value);
                console.log(holder[0].value);

                const prefixIndex: OperatorFixture = ++receiver()[index()];
                console.log(prefixIndex.value);
                console.log(holder[0].value);
                console.log(receiverCalls);
                console.log(indexCalls);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("6\n6\n7\n8\n8\n3\n2\n", output);
    }

    [Theory, ModeData]
    public void GenericClrMethods_InferAndAcceptExplicitTypeArguments(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { GenericMethodFixture } from "dotnet:SharpTS.Tests.Infrastructure.GenericMethodFixture";
                const fixture = new GenericMethodFixture();
                const inferredNumber: number = fixture.echo(42);
                const inferredString: string = GenericMethodFixture.staticEcho("hello");
                const explicitString: string = fixture.echo<string>("world");
                const explicitImported: GenericMethodFixture =
                    fixture.echo<GenericMethodFixture>(fixture);
                const constrained: number = fixture.constrained(7);
                console.log(inferredNumber);
                console.log(inferredString);
                console.log(explicitString);
                console.log(explicitImported.echo("imported"));
                console.log(constrained);
                console.log(fixture.typeName<number>());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\nhello\nworld\nimported\n7\nDouble\n", output);
    }

    [Theory, ModeData]
    public void GenericClrMethods_InferAndMarshalGuestArrays(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { GenericMethodFixture } from "dotnet:SharpTS.Tests.Infrastructure.GenericMethodFixture";
                const fixture = new GenericMethodFixture();
                const numbers: number[] = [1, 2, 3];
                const copied: number[] = fixture.copy(numbers);
                copied[0] = 9;
                console.log(copied.join(","));
                console.log(numbers.join(","));

                const words: string[] = fixture.copy(["alpha", "beta"]);
                console.log(words.map(word => word.toUpperCase()).join("|"));

                const nested: number[][] = fixture.copy([[1, 2], [3]]);
                console.log(nested.map(values => values.join("-")).join("|"));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("9,2,3\n1,2,3\nALPHA|BETA\n1-2|3\n", output);
    }

    [Theory, ModeData]
    public void GenericClrMethods_InferThroughCallbackSignatures(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { GenericMethodFixture } from "dotnet:SharpTS.Tests.Infrastructure.GenericMethodFixture";
                const fixture = new GenericMethodFixture();

                const transformed: number =
                    fixture.transform(5, value => value * 3);
                const generated: string =
                    fixture.fromFactory(() => "made");
                let observed = 0;
                fixture.tap(7, value => observed = value);

                console.log(transformed);
                console.log(generated);
                console.log(observed);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("15\nmade\n7\n", output);
    }

    [Theory, ModeData]
    public void GenericClrMethods_InferResultOnlyParametersFromContext(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { GenericMethodFixture } from "dotnet:SharpTS.Tests.Infrastructure.GenericMethodFixture";
                const fixture = new GenericMethodFixture();

                const value: number = fixture.defaultValue();
                const values: number[] = fixture.emptyArray();
                function obtain(): number {
                    return fixture.defaultValue();
                }

                console.log(value);
                console.log(values.length);
                console.log(obtain());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("0\n0\n0\n", output);
    }

    [Theory, ModeData]
    public void GenericExtensionMethods_AreInferredAndModuleScoped(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { List } from "dotnet:System.Collections.Generic.List<number>";
                import "dotnet-extensions:System.Linq.Enumerable";
                const values = new List();
                values.add(2.5);
                values.add(7.5);
                const first: number = values.first();
                console.log(values.count());
                console.log(first);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("2\n2.5\n", output);
    }

    [Fact]
    public void ExtensionMethods_DoNotLeakIntoOtherModules()
    {
        var errors = CheckErrors(new Dictionary<string, string>
        {
            ["./enabled.ts"] = """
                import { List } from "dotnet:System.Collections.Generic.List<number>";
                import "dotnet-extensions:SharpTS.Tests.Infrastructure.EnumerableExtensionFixture";
                export function enabled(values: List): string {
                    const result: string = values.countItems();
                    return result;
                }
                """,
            ["./disabled.ts"] = """
                import { List } from "dotnet:System.Collections.Generic.List<number>";
                export function disabled(values: List): string {
                    const result: string = values.countItems();
                    return result;
                }
                """,
            ["./main.ts"] = """
                import "./enabled";
                import "./disabled";
                """
        }, "./main.ts");

        Assert.Single(errors, d =>
            d.Message.Contains("not assignable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtensionContainer_RequiresSideEffectImport()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Enumerable } from "dotnet-extensions:System.Linq.Enumerable";
                console.log(Enumerable);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("side effects", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Namespace form, aliases, nested types

    [Theory, ModeData]
    public void NamespaceForm_ResolvesEachNamedImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Guid, Math as SysMath } from "dotnet:System";
                const g: Guid = Guid.newGuid();
                console.log(g.toString().length);
                console.log(SysMath.max(3, 9));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("36\n9\n", output);
    }

    [Theory, ModeData]
    public void AliasImport_BindsLocalName(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { StringBuilder as SB } from "dotnet:System.Text.StringBuilder";
                const sb: SB = new SB();
                sb.append("aliased");
                console.log(sb.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("aliased\n", output);
    }

    [Theory, ModeData]
    public void StaticReadonlyField_Resolves(ExecutionMode mode)
    {
        // Guid.empty is a static FIELD (not property) — the member surface must include
        // public fields, and the runtime lookup resolves them the same way.
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Guid } from "dotnet:System";
                console.log(Guid.empty.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("00000000-0000-0000-0000-000000000000\n", output);
    }

    [Theory, ModeData]
    public void NestedType_ResolvesThroughDeclaringTypeSpecifier(ExecutionMode mode)
    {
        // System.Environment is a type; SpecialFolder is its nested enum — the namespace-form
        // name resolution falls back to Declaring+Nested.
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { SpecialFolder } from "dotnet:System.Environment";
                console.log(SpecialFolder.Desktop.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Desktop\n", output);
    }

    [Theory, ModeData]
    public void EnumImport_MembersAndToString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { DayOfWeek } from "dotnet:System";
                const d: DayOfWeek = DayOfWeek.Monday;
                console.log(d.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Monday\n", output);
    }

    #endregion

    #region Cross-module and type identity

    [Theory, ModeData]
    public void CrossModule_InstanceFlowsBetweenModules(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./maker.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                export function makeSb(): StringBuilder {
                    const sb = new StringBuilder();
                    sb.append("from-maker");
                    return sb;
                }
                """,
            ["./main.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                import { makeSb } from "./maker";
                const sb: StringBuilder = makeSb();
                sb.append("+main");
                console.log(sb.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("from-maker+main\n", output);
    }

    [Theory, ModeData]
    public void BothSpecifierForms_YieldTheSameType(ExecutionMode mode)
    {
        // The synthesized class is cached per CLR type, so the namespace form and the
        // single-type form produce the identical TypeInfo — values assign across modules.
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                import { StringBuilder } from "dotnet:System.Text";
                export function make(): StringBuilder {
                    return new StringBuilder().append("same");
                }
                """,
            ["./main.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                import { make } from "./a";
                const sb: StringBuilder = make();
                console.log(sb.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("same\n", output);
    }

    [Theory, ModeData]
    public void TypeOnlyImport_UsableInAnnotations(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./maker.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                export function makeSb(): StringBuilder {
                    return new StringBuilder().append("typed");
                }
                """,
            ["./main.ts"] = """
                import type { StringBuilder } from "dotnet:System.Text.StringBuilder";
                import { makeSb } from "./maker";
                const sb: StringBuilder = makeSb();
                console.log(sb.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("typed\n", output);
    }

    #endregion

    #region Discovery-tool alignment

    [Theory, ModeData]
    public void DiscoveryToolImportLine_RoundTripsThroughThePipeline(ExecutionMode mode)
    {
        // The exact import line `--gen-decl` prints must load, type-check, and run —
        // the tool and the resolver share DotNetInteropClassifier, so they can never
        // disagree about importability (the #1192/#1193 lesson: round-trip, don't eyeball).
        var report = new DiscoveryGenerator().Generate("System.Text.StringBuilder");
        Assert.NotNull(report.Type?.ImportLine);

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = report.Type!.ImportLine + """

                const sb = new StringBuilder();
                sb.append("round-trip");
                console.log(sb.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("round-trip\n", output);
    }

    #endregion

    #region Error cases

    [Fact]
    public void UnknownName_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Nope } from "dotnet:System.NoSuchNs";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("Module Error", ex.Message);
        Assert.Contains("System.NoSuchNs", ex.Message);
    }

    [Fact]
    public void SingleTypeForm_WrongName_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Encoding } from "dotnet:System.Text.StringBuilder";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("exports only 'StringBuilder'", ex.Message);
    }

    [Fact]
    public void NamespaceStarImport_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as Text from "dotnet:System.Text";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("namespace imports", ex.Message);
    }

    [Fact]
    public void DefaultImport_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import StringBuilder from "dotnet:System.Text.StringBuilder";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("no default export", ex.Message);
    }

    [Fact]
    public void OpenGenericSpecifier_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { List } from "dotnet:System.Collections.Generic.List`1";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains(DotNetInteropClassifier.ReasonOpenGeneric, ex.Message);
    }

    [Fact]
    public void RefStructType_ThrowsModuleError()
    {
        // TypedReference is a public, non-generic ref struct in the core library —
        // rejected with the classifier's reason, exactly like --gen-decl reports it.
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { TypedReference } from "dotnet:System.TypedReference";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains(DotNetInteropClassifier.ReasonRefStruct, ex.Message);
    }

    [Fact]
    public void ReExportFrom_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                export { StringBuilder } from "dotnet:System.Text.StringBuilder";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("re-export", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportRequire_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import sb = require("dotnet:System.Text.StringBuilder");
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("named ESM import", ex.Message);
    }

    [Fact]
    public void NewOnStaticClass_IsATypeError()
    {
        // Static classes synthesize as abstract — `new` is rejected statically.
        var errors = CheckErrors(new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Console } from "dotnet:System.Console";
                const c = new Console();
                """
        }, "./main.ts");

        Assert.Contains(errors, d => d.Message.Contains("abstract", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
