using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class JsxForwardClassIdentityTests
{
    [Fact]
    public void ForwardChildrenAnnotationKeepsTheCompletedClassDeclaration()
    {
        string entry = Path.GetFullPath("jsx-forward-class-identity/main.tsx");
        var files = new Dictionary<string, string>
        {
            [entry] = """
                declare namespace JSX {
                    interface Element { brand: string; }
                    interface ElementClass { render(): Element; }
                    interface ElementAttributesProperty { props: {}; }
                    interface ElementChildrenAttribute { children: {}; }
                }
                interface Props { children: Button; }
                declare class Component<P> {
                    props: P; state: any; context: any; refs: any;
                    render(): JSX.Element;
                    setState(state: any): void;
                    forceUpdate(): void;
                }
                class Button extends Component<any> {
                    render(): JSX.Element { return {} as JSX.Element; }
                }
                declare function Comp(props: Props): JSX.Element;
                const element = <Comp><Button /></Comp>;
                const constructor = <Comp>{Button}</Comp>;
                const instance = <Comp>{new Button()}</Comp>;
                declare const holder: Props;
                holder.children;
                """,
        };
        var resolver = new ModuleResolver(entry, files, TypeScriptProgramOptions.Disabled)
        {
            JsxOptions = new JsxParseOptions(JsxMode.Preserve),
        };
        var program = resolver.LoadProgram(entry);
        var checker = new TypeChecker(strictNullChecks: true);
        var types = checker.CheckModules(resolver.GetModulesInOrder(program), resolver);
        Assert.Equal(new[] { "TS2740", "TS2740" }, checker.GetDiagnostics().Select(diagnostic => diagnostic.TsCode));
        var read = Assert.IsType<Expr.Get>(Assert.IsType<Stmt.Expression>(program.Statements.Last()).Expr);
        var retained = Assert.IsType<TypeInfo.Instance>(types.Get(read));
        var completed = Assert.IsType<TypeInfo.Class>(retained.ResolvedClassType);
        var declaration = Assert.Single(program.Statements.OfType<Stmt.Class>(), candidate => candidate.Name.Lexeme == "Button");
        Assert.Equal(types.GetClassType(declaration)!.Core.DeclarationId, completed.Core.DeclarationId);
        Assert.Contains("render", completed.Methods.Keys);
    }
}
