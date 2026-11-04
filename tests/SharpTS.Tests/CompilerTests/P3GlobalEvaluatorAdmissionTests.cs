using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class P3GlobalEvaluatorAdmissionTests
{
    [Theory]
    [InlineData("function expose(this:any){return this;}const owner:any=expose();const run:any=owner['ev'+'al'];run(String('40+2'));", true)]
    [InlineData("function expose(this:any){return this;}const alias:any=expose;const owner:any=alias();const run:any=owner.eval;run('40+2');", true)]
    [InlineData("function expose(this:any){return this;}const owner:any=expose();const next:any=owner;const key=String('eval');next[key]('40+2');", true)]
    [InlineData("const owner:any={value:1};console.log(owner['value']);", false)]
    [InlineData("function expose(){return {value:1};}const owner:any=expose();console.log(owner['value']);", false)]
    [InlineData("function expose(this:any){'use strict';return this;}const owner:any=expose();console.log(owner['value']);", false)]
    [InlineData("function expose(this:any){return this;}const owner:any=expose();{const owner:any={eval:()=>42};owner['eval']();}", false)]
    [InlineData("function expose(this:any){return this;}const owner:any=expose();console.log(owner['answer']);", false)]
    [InlineData("function expose(this:any){return (this);}const owner:any=expose();owner.eval('40+2');", true)]
    [InlineData("function expose(this:any){return (this as any);}const owner:any=expose();owner.eval('40+2');", true)]
    [InlineData("function expose(this:any){return (this satisfies any)!;}const owner:any=expose();owner.eval('40+2');", true)]
    [InlineData("function expose(this:any){return (0,this);}const owner:any=expose();owner.eval('40+2');", true)]
    [InlineData("function expose(this:any){return (()=>this)();}const owner:any=expose();owner.eval('40+2');", true)]
    [InlineData("function expose(this:any){return (()=>{return (this);})();}const owner:any=expose();owner.eval('40+2');", true)]
    [InlineData("const expose:any=()=>this;const owner:any=expose();owner.eval('40+2');", true)]
    [InlineData("'use strict';const expose:any=()=>this;const owner:any=expose();owner.eval('40+2');", true)]
    [InlineData("const expose:any=function(this:any){return (this);};const owner:any=expose();owner.eval('40+2');", true)]
    [InlineData("function expose(this:any){'use strict';return (()=>this)();}const owner:any=expose();owner.eval('40+2');", false)]
    [InlineData("const expose:any=function(this:any){'use strict';return (this);};const owner:any=expose();owner.eval('40+2');", false)]
    [InlineData("const expose:any=()=>({eval:()=>42});const owner:any=expose();owner.eval();", false)]
    [InlineData("const C=class{method(){function expose(this:any){return (this);}const owner:any=expose();owner.eval('40+2');}};", false)]
    [InlineData("const C=class{method(){const expose:any=()=>this;const owner:any=expose();owner.eval('40+2');}};", false)]
    [InlineData("export const expose:any=()=>this;const owner:any=expose();owner.eval('40+2');", false)]
    public void AdmissionRequiresActualGlobalReceiverProvenance(string source, bool requiresRuntime)
    {
        var statements = TestHarness.ParseOrThrow(source);
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("GlobalProvenance");
        compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        Assert.Equal(requiresRuntime, compiler.RequiredSharpTSRuntimeRequirements != SharpTSRuntimeRequirements.None);
        if (requiresRuntime)
            Assert.Contains(compiler.RequiredSharpTSRuntimeReasons, reason => reason.Contains("eval"));
    }
}
