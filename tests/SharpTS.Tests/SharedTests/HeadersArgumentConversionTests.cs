using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class HeadersArgumentConversionTests
{
    private const string MutableProgram = """
        const headers = new Headers();
        function name() { return {toString() { console.log('name'); return 'x-tag'; }}; }
        function value() { return {toString() { console.log('value'); return 'one'; }}; }
        headers.set(name(), value());
        headers.append(name(), {toString() { return {}; }, valueOf() { console.log('fallback'); return 'two'; }});
        console.log(headers.get(name()));
        console.log(headers.has(name()));
        headers.delete(name());
        console.log(headers.has('x-tag'));
        headers.set(null, undefined);
        headers.set(false, 42);
        headers.set('big', 12n);
        console.log(headers.get(null));
        console.log(headers.get(false));
        console.log(headers.get('big'));
        try { headers.set({toString() { throw new Error('name-failed'); }}, value()); }
        catch(e) { console.log(e.message); }
        try { headers.append(name(), {toString() { throw new Error('value-failed'); }}); }
        catch(e) { console.log(e.message); }
        console.log(headers.has('x-tag'));
        try { headers.get(Symbol('name')); } catch(e) { console.log(e.name); }
        try { headers.set('x-tag', Symbol('value')); } catch(e) { console.log(e.name); }
        try { headers.has({toString() { return {}; }, valueOf() { return {}; }}); }
        catch(e) { console.log(e.name); }
        console.log(headers.has('x-tag'));
        """;

    private const string FetchedProgram = """
        function check(headers) {
            function name() { return {toString() { console.log('name'); return 'x-tag'; }}; }
            function value() { return {toString() { console.log('value'); return 'changed'; }}; }
            try { headers.append(name(), value()); } catch(e) { console.log(e.name); }
            try { headers.set(name(), value()); } catch(e) { console.log(e.name); }
            try { headers.delete(name()); } catch(e) { console.log(e.name); }
            try { headers.set({toString() { throw new Error('name-failed'); }}, value()); }
            catch(e) { console.log(e.message); }
            try { headers.append(name(), {toString() { throw new Error('value-failed'); }}); }
            catch(e) { console.log(e.message); }
            console.log(headers.has('x-tag'));
        }
        async function main() {
            const response = await fetch('URL');
            check(response.headers);
            check(response.clone().headers);
        }
        main();
        """;

    [Theory]
    [InlineData("interpreted")]
    [InlineData("compiled")]
    [InlineData("standalone")]
    public void HeaderMethodsUseGuestConversionAndPropagateAbruptCompletion(string mode)
    {
        Assert.Equal("name\nvalue\nname\nfallback\nname\none, two\nname\ntrue\nname\nfalse\n"
            + "undefined\n42\n12\nname-failed\nname\nvalue-failed\nfalse\nTypeError\nTypeError\nTypeError\nfalse\n",
            Run(MutableProgram, mode));
    }

    [Theory]
    [InlineData("interpreted")]
    [InlineData("compiled")]
    [InlineData("standalone")]
    public void FetchedHeaderConversionPrecedesImmutableGuard(string mode)
    {
        using var server = new MockHttpServer();
        server.AddTextRoute("/body", "body");
        server.Start();
        var source = FetchedProgram.Replace("URL", server.BaseUrl + "body");
        var expected = "name\nvalue\nTypeError\nname\nvalue\nTypeError\nname\nTypeError\n"
            + "name-failed\nname\nvalue-failed\nfalse\n";
        Assert.Equal(expected + expected, Run(source, mode));
    }

    private static string Run(string source, string mode) => mode == "standalone"
        ? TestHarness.RunCompiledStandalone(source)
        : TestHarness.Run(source, mode == "compiled" ? ExecutionMode.Compiled : ExecutionMode.Interpreted);
}
