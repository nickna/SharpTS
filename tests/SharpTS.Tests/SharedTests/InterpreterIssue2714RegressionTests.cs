using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2714RegressionTests
{
    [Theory, ModeData]
    public void OriginalIssueSourceRemainsCorrect(ExecutionMode mode)
    {
        const string source = "const C = class {};\nconst prototype: any = C.prototype;\nObject.defineProperty(prototype, \"locked\", { value: 9, configurable: false });\nfunction strictRemove(target: any) {\n    \"use strict\";\n    try { console.log(delete target.locked); }\n    catch (error) { console.log(\"threw\", error instanceof TypeError); }\n}\nstrictRemove(prototype);\nconsole.log(Object.hasOwn(prototype, \"locked\"), prototype.locked);\nconst record: any = {};\nObject.defineProperty(record, \"locked\", { value: 9, configurable: false });\nstrictRemove(record);\n";
        Assert.Equal("threw true\ntrue 9\nthrew true\n", TestHarness.Run(source, mode));
    }
}