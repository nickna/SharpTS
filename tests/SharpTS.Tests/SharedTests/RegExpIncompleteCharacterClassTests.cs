using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RegExpIncompleteCharacterClassTests
{
    [Theory, ModeData]
    public void UnterminatedClassesThrowGuestSyntaxError(ExecutionMode mode)
    {
        const string source = """
            for(const pattern of ['[','[^','[\\W','[^\\W']){
                try{new RegExp(pattern);console.log('accepted');}catch(error){console.log(error instanceof SyntaxError);}
            }
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrefixesAndFlagsPreserveTheGuestErrorAndEscapedBracketControls(ExecutionMode mode)
    {
        const string source = """
            let syntaxErrors=0;
            for(const prefix of ['', 'x'])for(const flags of ['', 'u', 's']){
                for(const pattern of ['[','[^','[\\W','[^\\W']){
                    try{new RegExp(prefix+pattern,flags);console.log('accepted');}
                    catch(error){if(error instanceof SyntaxError)syntaxErrors++;else console.log('wrong error');}
                }
            }
            console.log(syntaxErrors,new RegExp('\\[').test('['),new RegExp('[\\]]').test(']'),new RegExp('[a]').test('a'));
            """;
        Assert.Equal("24 true true true\n", TestHarness.Run(source, mode));
    }
}
