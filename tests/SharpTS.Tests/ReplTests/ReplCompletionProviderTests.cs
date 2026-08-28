using SharpTS.Parsing;
using SharpTS.Repl;
using Xunit;

namespace SharpTS.Tests.ReplTests;

/// <summary>
/// Tests for REPL autocomplete. Candidate computation lives in <see cref="ReplCompletionProvider"/>
/// precisely so it can be exercised here without a console — <c>ReplEngine.RunAsync</c> blocks on a
/// real PrettyPrompt instance, and the callbacks themselves are protected.
/// </summary>
public class ReplCompletionProviderTests
{
    /// <summary>
    /// Builds a REPL session by running each line through the real
    /// parse → resolve → interpret → accumulate pipeline, so tests see genuine session state.
    /// </summary>
    private static ReplEngine Session(params string[] lines)
    {
        var engine = new ReplEngine(DecoratorMode.Stage3);
        foreach (var line in lines)
            engine.ExecuteInput(line);
        return engine;
    }

    private static List<string> Names(ReplEngine engine, string text) =>
        engine.Completions.GetCandidates(text, text.Length).Select(c => c.Name).ToList();

    // ===================== Context classification =====================

    [Theory]
    [InlineData("foo.", "foo", "")]
    [InlineData("foo.ba", "foo", "ba")]
    [InlineData("foo?.ba", "foo", "ba")]
    [InlineData("foo.bar.ba", "foo.bar", "ba")]
    [InlineData("getUser().", "getUser()", "")]
    [InlineData("arr[0].", "arr[0]", "")]
    [InlineData("(a ?? b).", "(a ?? b)", "")]
    [InlineData("new C().", "new C()", "")]
    [InlineData("x = foo.", "foo", "")]
    [InlineData("1 + foo.ba", "foo", "ba")]
    [InlineData("console.log(d.", "d", "")]
    public void Classify_MemberContext_ExtractsReceiverAndPartial(
        string text, string expectedReceiver, string expectedPartial)
    {
        var context = ReplCompletionContext.Classify(text, text.Length);

        Assert.Equal(ReplCompletionContextKind.Member, context.Kind);
        Assert.Equal(expectedReceiver, context.Receiver);
        Assert.Equal(expectedPartial, context.Partial);
    }

    [Theory]
    // Keyword lexemes are legal property names, so these must still complete as members.
    [InlineData("m.get", "get")]
    [InlineData("p.type", "type")]
    [InlineData("s.default", "default")]
    [InlineData("p.catch", "catch")]
    public void Classify_KeywordNamedMember_IsStillAMember(string text, string expectedPartial)
    {
        var context = ReplCompletionContext.Classify(text, text.Length);

        Assert.Equal(ReplCompletionContextKind.Member, context.Kind);
        Assert.Equal(expectedPartial, context.Partial);
    }

    [Theory]
    [InlineData("\"hello wor")]        // unterminated string
    [InlineData("'hello wor")]
    [InlineData("`hello wor")]         // unterminated template
    [InlineData("\"abc\".x + \"de")]
    [InlineData("// comm")]            // line comment
    [InlineData("x + // comm")]
    [InlineData("/* comm")]            // unterminated block comment
    [InlineData("obj. // trailing")]
    [InlineData("1.")]                 // numeric literal, not a member access
    [InlineData("1.5")]
    [InlineData("\"abc\"")]            // directly after a closed literal
    public void Classify_SuppressedPositions_OfferNothing(string text)
    {
        var context = ReplCompletionContext.Classify(text, text.Length);

        Assert.Equal(ReplCompletionContextKind.None, context.Kind);
    }

    [Fact]
    public void Classify_InsideTemplateInterpolation_CompletesAsMember()
    {
        // `${` opens real code, so completion should work inside it.
        var context = ReplCompletionContext.Classify("`total: ${obj.", 14);

        Assert.Equal(ReplCompletionContextKind.Member, context.Kind);
        Assert.Equal("obj", context.Receiver);
    }

    [Fact]
    public void Classify_AfterClosedComment_StillCompletes()
    {
        var context = ReplCompletionContext.Classify("x + /* c */ fo", 14);

        Assert.Equal(ReplCompletionContextKind.Identifier, context.Kind);
        Assert.Equal("fo", context.Partial);
    }

    [Fact]
    public void Classify_DollarPrefixedIdentifier_ReplacesTheWholeWord()
    {
        // The default word detection excludes '$', which would commit "$foo" over "fo" as "$$foo".
        var context = ReplCompletionContext.Classify("$fo", 3);

        Assert.Equal(ReplCompletionContextKind.Identifier, context.Kind);
        Assert.Equal("$fo", context.Partial);
        Assert.Equal(0, context.ReplaceStart);
    }

    [Fact]
    public void Classify_CaretInsideWord_UsesTextBeforeCaretOnly()
    {
        var context = ReplCompletionContext.Classify("foo.bar", 6);

        Assert.Equal(ReplCompletionContextKind.Member, context.Kind);
        Assert.Equal("foo", context.Receiver);
        Assert.Equal("ba", context.Partial);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_EmptyInput_IsAnUnfilteredIdentifierContext(string text)
    {
        var context = ReplCompletionContext.Classify(text, text.Length);

        Assert.Equal(ReplCompletionContextKind.Identifier, context.Kind);
        Assert.Equal("", context.Partial);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a??.")]
    [InlineData("=>.")]
    [InlineData("0x")]
    [InlineData("1_")]
    [InlineData("/re")]
    [InlineData("#bad")]
    [InlineData("}.")]
    [InlineData("({}).x.")]
    public void GetCandidates_HostileInput_NeverThrows(string text)
    {
        using var engine = Session();

        // The assertion is that this does not throw; an empty list is a fine answer.
        Assert.NotNull(engine.Completions.GetCandidates(text, text.Length));
    }

    // ===================== Member completion via the type checker =====================

    [Fact]
    public void Members_OfString_ComeFromTheStringApparentMembers()
    {
        using var engine = Session("const s = \"x\";");

        var names = Names(engine, "s.");

        Assert.Contains("charAt", names);
        Assert.Contains("toUpperCase", names);
        Assert.Contains("length", names);
    }

    [Fact]
    public void Members_OfArray_ComeFromTheArrayApparentMembers()
    {
        using var engine = Session("const a = [1, 2, 3];");

        var names = Names(engine, "a.");

        Assert.Contains("push", names);
        Assert.Contains("map", names);
        Assert.Contains("length", names);
    }

    [Fact]
    public void Members_OfObjectLiteral_AreItsOwnFields()
    {
        using var engine = Session("const p = { name: \"a\", age: 1 };");

        var names = Names(engine, "p.");

        Assert.Contains("name", names);
        Assert.Contains("age", names);
    }

    [Fact]
    public void Members_OfClassInstance_IncludePublicMembersOnly()
    {
        using var engine = Session(
            "class C { x = 1; m() {} get g() { return 1; } private secret = 2; }",
            "const c = new C();");

        var names = Names(engine, "c.");

        Assert.Contains("x", names);
        Assert.Contains("m", names);
        Assert.Contains("g", names);
        Assert.DoesNotContain("secret", names);
        Assert.DoesNotContain("constructor", names);
    }

    [Fact]
    public void Members_OfSubclassInstance_IncludeInheritedMembers()
    {
        // Regression guard: keyof-style enumeration does not walk the superclass chain, so
        // inherited members must come from the class-specific collector instead.
        using var engine = Session(
            "class Base { baseMethod() {} }",
            "class Derived extends Base { derivedMethod() {} }",
            "const d = new Derived();");

        var names = Names(engine, "d.");

        Assert.Contains("derivedMethod", names);
        Assert.Contains("baseMethod", names);
    }

    [Fact]
    public void Members_OfClassReference_AreStaticsNotInstanceMembers()
    {
        using var engine = Session("class C { static create() {} instanceMethod() {} }");

        var names = Names(engine, "C.");

        Assert.Contains("create", names);
        Assert.DoesNotContain("instanceMethod", names);
    }

    [Fact]
    public void Members_OfInterfaceTypedBinding_IncludeInheritedMembers()
    {
        using var engine = Session(
            "interface A { a: number; }",
            "interface B extends A { b: number; }",
            "const value: B = { a: 1, b: 2 };");

        var names = Names(engine, "value.");

        Assert.Contains("b", names);
        Assert.Contains("a", names);
    }

    [Fact]
    public void Members_OfCallResult_ResolveThroughTheExpression()
    {
        // The motivating case for resolving receivers through the type checker: a call result has
        // no live value to inspect until it is evaluated.
        using var engine = Session("function makeText(): string { return \"x\"; }");

        var names = Names(engine, "makeText().");

        Assert.Contains("charAt", names);
    }

    [Fact]
    public void Members_OfIndexedElement_ResolveThroughTheExpression()
    {
        using var engine = Session("const words = [\"a\", \"b\"];");

        var names = Names(engine, "words[0].");

        Assert.Contains("charAt", names);
    }

    [Fact]
    public void Members_OfNestedProperty_ResolveThroughTheChain()
    {
        using var engine = Session("const o = { inner: { deep: \"s\" } };");

        var names = Names(engine, "o.inner.deep.");

        Assert.Contains("charAt", names);
    }

    [Fact]
    public void Members_OfMapInstance_ComeFromTheBuiltInModel()
    {
        using var engine = Session("const m = new Map<string, number>();");

        var names = Names(engine, "m.");

        Assert.Contains("get", names);
        Assert.Contains("set", names);
        Assert.Contains("has", names);
    }

    [Fact]
    public void Members_OfUnionTypedBinding_AreTheCommonMembersOnly()
    {
        // Keep both constituents reachable. A direct `{ a, b }` initializer correctly flow-narrows
        // the binding to that constituent, in which case offering `b` is expected.
        using var engine = Session(
            "let u: { a: number; b: number } | { a: number; c: number } = " +
            "Math.random() < 0.5 ? { a: 1, b: 2 } : { a: 1, c: 2 };");

        var names = Names(engine, "u.");

        Assert.Contains("a", names);
        Assert.DoesNotContain("b", names);
        Assert.DoesNotContain("c", names);
    }

    [Fact]
    public void Members_CarryTheirTypeAsTooltipDetail()
    {
        using var engine = Session("const p = { name: \"a\" };");

        var name = engine.Completions
            .GetCandidates("p.", 2)
            .Single(c => c.Name == "name");

        Assert.Equal(ReplCompletionKind.Member, name.Kind);
        Assert.NotNull(name.Detail);
        Assert.Contains("string", name.Detail);
    }

    [Fact]
    public void Members_TooltipsAreSingleLineAndBounded()
    {
        // Tooltip text goes straight into the description pane; a recursive or deeply generic type
        // can render to something enormous or multi-line.
        using var engine = Session(
            "interface Big { a: string; b: number; c: (x: string, y: number) => string; }",
            "const big: Big = { a: \"\", b: 0, c: (x, y) => x };");

        var details = engine.Completions
            .GetCandidates("big.", 4)
            .Select(c => c.Detail)
            .Where(d => d is not null)
            .ToList();

        Assert.NotEmpty(details);
        Assert.All(details, d =>
        {
            Assert.DoesNotContain('\n', d!);
            Assert.DoesNotContain('\r', d!);
            Assert.True(d!.Length <= 160, $"tooltip too long: {d.Length}");
        });
    }

    [Fact]
    public void Members_OfUnknownReceiver_AreEmpty()
    {
        using var engine = Session();

        Assert.Empty(Names(engine, "definitelyNotDefined."));
    }

    // ===================== Built-in singletons =====================

    [Theory]
    [InlineData("console.", "log")]
    [InlineData("console.", "error")]
    [InlineData("Math.", "floor")]
    [InlineData("Math.", "max")]
    [InlineData("JSON.", "parse")]
    [InlineData("JSON.", "stringify")]
    [InlineData("Object.", "keys")]
    public void Members_OfAnyTypedSingletons_FallBackToTheRuntimeTables(string text, string expected)
    {
        // The checker types console/Math/JSON/Object as `any`, so these would offer nothing at all
        // without the runtime member-table fallback.
        using var engine = Session();

        Assert.Contains(expected, Names(engine, text));
    }

    // ===================== Identifiers, globals, keywords =====================

    [Fact]
    public void Identifiers_IncludeSessionBindings()
    {
        using var engine = Session("const myThing = 1;", "function myFunc() {}", "class MyClass {}");

        var names = Names(engine, "my");

        Assert.Contains("myThing", names);
        Assert.Contains("myFunc", names);
        Assert.Contains("MyClass", names);
    }

    [Fact]
    public void Identifiers_IncludeGlobalsAndKeywords()
    {
        using var engine = Session();

        var names = Names(engine, "c");

        Assert.Contains("console", names);
        Assert.Contains("const", names);
        Assert.Contains("Math", names);
    }

    [Fact]
    public void Identifiers_AreCategorisedForRanking()
    {
        using var engine = Session("const myThing = 1;");

        var candidates = engine.Completions.GetCandidates("m", 1);

        Assert.Equal(
            ReplCompletionKind.Binding,
            candidates.Single(c => c.Name == "myThing").Kind);
        Assert.Equal(
            ReplCompletionKind.Global,
            candidates.Single(c => c.Name == "Math").Kind);
        Assert.Equal(
            ReplCompletionKind.Keyword,
            candidates.Single(c => c.Name == "module").Kind);
    }

    [Fact]
    public void Identifiers_ShadowedNameAppearsOnce()
    {
        using var engine = Session("const Math = 1;");

        var matches = engine.Completions.GetCandidates("Ma", 2).Where(c => c.Name == "Math").ToList();

        Assert.Single(matches);
        Assert.Equal(ReplCompletionKind.Binding, matches[0].Kind);
    }

    [Fact]
    public void Identifiers_AreNotOfferedInAMemberPosition()
    {
        using var engine = Session("const s = \"x\";");

        var names = Names(engine, "s.");

        Assert.DoesNotContain("console", names);
        Assert.DoesNotContain("const", names);
    }

    // ===================== Dot-commands and paths =====================

    [Fact]
    public void DotCommands_AreOfferedAtTheStartOfALine()
    {
        using var engine = Session();

        var names = Names(engine, ".");

        Assert.Contains(".help", names);
        Assert.Contains(".exit", names);
        Assert.Contains(".type", names);
        Assert.Equal(DotCommands.Commands.Length, names.Count);
    }

    [Fact]
    public void DotCommands_ReplacementSpanCoversTheLeadingDot()
    {
        // Otherwise committing ".help" over ".he" would produce "..help".
        var context = ReplCompletionContext.Classify(".he", 3);

        Assert.Equal(ReplCompletionContextKind.DotCommand, context.Kind);
        Assert.Equal(0, context.ReplaceStart);
        Assert.Equal(".he", context.Partial);
    }

    [Fact]
    public void DotCommands_CarryTheirHelpTextAsTooltipDetail()
    {
        using var engine = Session();

        var help = engine.Completions.GetCandidates(".", 1).Single(c => c.Name == ".help");

        Assert.Equal(ReplCompletionKind.DotCommand, help.Kind);
        Assert.Equal("Show this help message", help.Detail);
    }

    [Fact]
    public void DotCommands_NonFileCommandArgumentsOfferNothing()
    {
        var context = ReplCompletionContext.Classify(".exit now", 9);

        Assert.Equal(ReplCompletionContextKind.None, context.Kind);
    }

    [Theory]
    [InlineData(".load ")]
    [InlineData(".save ./")]
    public void DotCommands_FileArgumentsAreAPathContext(string text)
    {
        var context = ReplCompletionContext.Classify(text, text.Length);

        Assert.Equal(ReplCompletionContextKind.DotCommandArgument, context.Kind);
    }

    [Fact]
    public void DotCommands_LoadOffersFilesystemEntries()
    {
        using var engine = Session();

        var candidates = engine.Completions.GetCandidates(".load ", 6);

        // The working directory always has something in it; the point is that paths are offered.
        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.Equal(ReplCompletionKind.FilePath, c.Kind));
    }

    // ===================== Trigger and commit policy =====================

    [Theory]
    [InlineData("co", true)]            // typing a word suggests as you go
    [InlineData("obj.", true)]          // and immediately after a dot
    [InlineData("obj.ba", true)]
    [InlineData(".he", true)]           // and for dot-commands
    [InlineData("", false)]             // but not on a bare prompt
    [InlineData("x + ", false)]         // nor after an operator with no word started
    [InlineData("\"hello wor", false)]  // never inside a string
    [InlineData("// note", false)]      // nor a comment
    [InlineData("1.", false)]           // nor after a numeric literal
    public void TriggerPolicy_OpensOnlyWhereCompletionApplies(string text, bool expected)
    {
        Assert.Equal(expected, ReplCallbacks.ShouldOpenCompletionWindow(text, text.Length));
    }

    [Fact]
    public void CommitPolicy_TabAlwaysCommits()
    {
        Assert.True(ReplCallbacks.ShouldCommitCompletion(ConsoleKey.Tab, "console"));
        Assert.True(ReplCallbacks.ShouldCommitCompletion(ConsoleKey.Tab, "function f() {"));
    }

    [Fact]
    public void CommitPolicy_EnterSubmitsAFinishedLineInsteadOfCommitting()
    {
        // Otherwise the menu, which is open most of the time, would swallow every Enter.
        Assert.False(ReplCallbacks.ShouldCommitCompletion(ConsoleKey.Enter, "console"));
    }

    [Fact]
    public void CommitPolicy_EnterStillCommitsWhileInputIsIncomplete()
    {
        // Load-bearing: PrettyPrompt skips the Enter-to-newline transform whenever a key would
        // commit a completion, so rejecting Enter here would break multi-line editing. Restricting
        // the rejection to complete input keeps the bypass confined to cases where that transform
        // would have done nothing.
        Assert.True(ReplCallbacks.ShouldCommitCompletion(ConsoleKey.Enter, "function f() {"));
        Assert.True(ReplCallbacks.ShouldCommitCompletion(ConsoleKey.Enter, "foo(bar"));
    }

    // ===================== PrettyPrompt item mapping =====================

    /// <summary>
    /// Invokes a protected <c>PromptCallbacks</c> override. PrettyPrompt's <c>Prompt</c> cannot be
    /// constructed without a real terminal (it fails in console-mode setup), so the callbacks are
    /// driven directly to cover the candidate-to-<c>CompletionItem</c> mapping that the provider
    /// tests above do not reach.
    /// </summary>
    private static object InvokeCallback(ReplCallbacks callbacks, string name, params object?[] args)
    {
        var method = typeof(ReplCallbacks).GetMethod(
            name,
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public)
            ?? throw new InvalidOperationException($"{name} not found");

        var task = method.Invoke(callbacks, args)
            ?? throw new InvalidOperationException($"{name} returned null");

        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    [Fact]
    public void Callbacks_MapCandidatesToUsableCompletionItems()
    {
        using var engine = Session("const greeting = \"hi\";");
        var callbacks = new ReplCallbacks(engine.Completions);

        var items = (IReadOnlyList<PrettyPrompt.Completion.CompletionItem>)InvokeCallback(
            callbacks,
            "GetCompletionItemsAsync",
            "greeting.", 9, new PrettyPrompt.Documents.TextSpan(9, 0), CancellationToken.None);

        var charAt = items.Single(i => i.ReplacementText == "charAt");
        Assert.Equal("charAt", charAt.FilterText);
        Assert.Equal("charAt", charAt.DisplayText);
    }

    [Fact]
    public void Callbacks_RankSessionBindingsAboveGlobalsAndKeywords()
    {
        using var engine = Session("const cx = 1;");
        var callbacks = new ReplCallbacks(engine.Completions);
        var span = new PrettyPrompt.Documents.TextSpan(0, 1);

        var items = (IReadOnlyList<PrettyPrompt.Completion.CompletionItem>)InvokeCallback(
            callbacks, "GetCompletionItemsAsync", "c", 1, span, CancellationToken.None);

        int Priority(string name) =>
            items.Single(i => i.ReplacementText == name).GetCompletionItemPriority("c", 1, span);

        Assert.True(Priority("cx") > Priority("crypto"));
        Assert.True(Priority("crypto") > Priority("class"));
    }

    [Fact]
    public void Callbacks_ReplacementSpanCoversADollarPrefixedWord()
    {
        using var engine = Session();
        var callbacks = new ReplCallbacks(engine.Completions);

        var span = (PrettyPrompt.Documents.TextSpan)InvokeCallback(
            callbacks, "GetSpanToReplaceByCompletionAsync", "$fo", 3, CancellationToken.None);

        Assert.Equal(0, span.Start);
        Assert.Equal(3, span.Length);
    }

    // ===================== Session lifecycle =====================

    [Fact]
    public void Completion_ReflectsBindingsAddedAfterTheProviderWasBuilt()
    {
        using var engine = Session();
        Assert.DoesNotContain("later", Names(engine, "la"));

        engine.ExecuteInput("const later = \"x\";");

        Assert.Contains("later", Names(engine, "la"));
        Assert.Contains("charAt", Names(engine, "later."));
    }

    [Fact]
    public void Completion_MemberListIsNotStaleAfterARedeclaration()
    {
        using var engine = Session("const v = \"x\";");
        Assert.Contains("charAt", Names(engine, "v."));

        engine.ExecuteInput("const v2 = [1];");

        Assert.Contains("push", Names(engine, "v2."));
    }
}
