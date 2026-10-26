using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class SwitchDefaultOrderingIlTests
{
    public static IEnumerable<object[]> Programs() => SwitchDefaultOrderingTests.Programs().Where(row =>
        (string)row[0] is "middle-fallthrough" or "case-effects-stop-after-match" or "labeled-loop-exits"
            or "return-through-finally" or "shared-clause-lexical-tdz" or "case-test-later-lexical-shadow"
            or "case-test-hoisted-function" or "awaited-tests-and-bodies"
            or "named-cross-clause-capture" or "named-cross-clause-live-binding"
            or "yielded-tests-and-bodies" or "generator-case-destructuring-spill" or "async-generator-body-suspension")
        .Select(row => new object[] { row[1] });

    [Theory, MemberData(nameof(Programs))]
    public void OrderedClausesProduceVerifiedIl(string source)
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
}
