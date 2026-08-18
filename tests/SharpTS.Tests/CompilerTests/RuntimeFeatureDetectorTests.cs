using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class RuntimeFeatureDetectorTests
{
    [Theory]
    [InlineData("async function f() { for await (const x of [1]) {} }", true)]
    [InlineData("function f() { for (const x of [1]) {} }", false)]
    public void DetectsForAwaitOfAdapterRequirement(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);

        Assert.Equal(expected, features.UsesForAwaitOf);
    }

    [Theory]
    [InlineData("Object.defineProperty([], '0', { get() { return 1; } });", true)]
    [InlineData("Object.defineProperties([], {});", true)]
    [InlineData("Object.create(null, {});", true)]
    [InlineData("Reflect.defineProperty([], '0', { value: 1 });", true)]
    [InlineData("const define = Object.defineProperty; define([], '0', { value: 1 });", true)]
    [InlineData("Object['defineProperty']([], '0', { value: 1 });", true)]
    [InlineData("Object.keys([]);", false)]
    public void DetectsDynamicPropertyDescriptorUsage(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);

        Assert.Equal(expected, features.UsesDynamicPropertyDescriptors);
    }

    [Theory]
    [InlineData("Date.prototype.toString = Object.prototype.toString;", true)]
    [InlineData("Date.prototype['valueOf'] = function() { return 0; };", true)]
    [InlineData("Object.defineProperty(Date.prototype, 'x', { value: 1 });", true)]
    [InlineData("new Date().toString();", false)]
    public void DetectsDatePrototypeMutation(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);

        Assert.Equal(expected, features.UsesDatePrototypeMutation);
    }
}
