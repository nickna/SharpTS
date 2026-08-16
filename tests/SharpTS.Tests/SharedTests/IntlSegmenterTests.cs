using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Intl.Segmenter API.
/// Tests run against both interpreter and compiler modes.
/// </summary>
public class IntlSegmenterTests
{
    // ========== Grapheme Segmentation ==========

    [Theory, ModeData]
    public void Segmenter_GraphemeBasic(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""grapheme""});
            const segments = seg.segment(""Hello"");
            let count = 0;
            for (const s of segments) { count++; }
            console.log(count);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory, ModeData]
    public void Segmenter_GraphemeSpread(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""grapheme""});
            const arr = [...seg.segment(""Hi"")];
            console.log(arr.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void Segmenter_GraphemeSegmentProperties(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""grapheme""});
            const segments = [...seg.segment(""AB"")];
            console.log(segments[0].segment);
            console.log(segments[0].index);
            console.log(segments[1].segment);
            console.log(segments[1].index);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("A\n0\nB\n1\n", output);
    }

    // ========== Word Segmentation ==========

    [Theory, ModeData]
    public void Segmenter_WordBasic(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""word""});
            const segments = [...seg.segment(""Hello World"")];
            console.log(segments.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output); // "Hello", " ", "World"
    }

    [Theory, ModeData]
    public void Segmenter_WordIsWordLike(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""word""});
            const segments = [...seg.segment(""Hello World"")];
            console.log(segments[0].isWordLike);
            console.log(segments[1].isWordLike);
            console.log(segments[2].isWordLike);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\n", output);
    }

    // ========== Sentence Segmentation ==========

    [Theory, ModeData]
    public void Segmenter_SentenceBasic(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""sentence""});
            const segments = [...seg.segment(""Hello. World."")];
            console.log(segments.length);
            console.log(segments[0].segment);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\nHello.\n", output);
    }

    // ========== Resolved Options ==========

    [Theory, ModeData]
    public void Segmenter_ResolvedOptions(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en-US"", {granularity: ""word""});
            const opts = seg.resolvedOptions();
            console.log(opts.granularity);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("word\n", output);
    }

    [Theory, ModeData]
    public void Segmenter_DefaultGranularity(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"");
            const opts = seg.resolvedOptions();
            console.log(opts.granularity);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("grapheme\n", output);
    }

    // ========== Segment Index Values ==========

    [Theory, ModeData]
    public void Segmenter_SegmentIndex(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""grapheme""});
            const segments = [...seg.segment(""abc"")];
            console.log(segments[0].index);
            console.log(segments[1].index);
            console.log(segments[2].index);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n", output);
    }

    // ========== Containing Method ==========

    [Theory, ModeData]
    public void Segmenter_ContainingBasic(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""grapheme""});
            const segments = seg.segment(""Hello"");
            const first = segments.containing(0);
            console.log(first.segment);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("H\n", output);
    }

    [Theory, ModeData]
    public void Segmenter_ContainingMiddle(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""grapheme""});
            const segments = seg.segment(""Hello"");
            const mid = segments.containing(2);
            console.log(mid.segment);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("l\n", output);
    }

    // ========== Empty String ==========

    [Theory, ModeData]
    public void Segmenter_EmptyString(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""grapheme""});
            const segments = [...seg.segment("""")];
            console.log(segments.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    // ========== For-Of Iteration ==========

    [Theory, ModeData]
    public void Segmenter_ForOf(ExecutionMode mode)
    {
        var source = @"
            const seg = new Intl.Segmenter(""en"", {granularity: ""grapheme""});
            const result: string[] = [];
            for (const s of seg.segment(""abc"")) {
                result.push(s.segment);
            }
            console.log(result.join("",""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("a,b,c\n", output);
    }
}
