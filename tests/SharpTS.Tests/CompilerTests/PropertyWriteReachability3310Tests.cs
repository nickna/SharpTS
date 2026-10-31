using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class PropertyWriteReachability3310Tests
{
    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("const value = {slot: 1}; console.log(value.slot);", false)]
    [InlineData("const value:any={};value.slot=1;", true)]
    [InlineData("const value:any={},key='slot';value[key]=1;", true)]
    [InlineData("const value:any={};value.slot+=1;", true)]
    [InlineData("const value:any={},key='slot';value[key]+=1;", true)]
    [InlineData("const value:any={};value.slot||=1;", true)]
    [InlineData("const value:any={},key='slot';value[key]??=1;", true)]
    [InlineData("const value:any={};++value.slot;", true)]
    [InlineData("const value:any={},key='slot';value[key]++;", true)]
    [InlineData("async function write(value:any){await Promise.resolve();value.slot=1;}", true)]
    [InlineData("function* write(value:any){yield 1;value.slot=1;}", true)]
    [InlineData("export function write(value:any){value.slot=1;}", true)]
    [InlineData("const write=(value:any)=>{value.slot=1;};", true)]
    [InlineData("const write=async(value:any)=>{value['slot']=await Promise.resolve(1);};", true)]
    [InlineData("const value:any={};({slot:value.slot}={slot:1});", true)]
    public void AllSourceWriteFormsRetainTheAssignmentProtocol(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        Assert.Equal(expected, new RuntimeFeatureDetector().Detect(statements).UsesPropertyWrites);
    }

    [Fact]
    public void ConservativeFeatureSetsRetainAssignmentProtocol()
    {
        Assert.True(new RuntimeFeatureSet().UsesPropertyWrites);
        Assert.True(RuntimeFeatureSet.EmitEverything().UsesPropertyWrites);
    }
}