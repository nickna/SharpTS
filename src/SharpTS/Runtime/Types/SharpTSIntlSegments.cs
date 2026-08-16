using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Intl.Segments — the iterable result of Segmenter.segment().
/// Implements IEnumerable&lt;object?&gt; for compiled for-of/spread compatibility via IterateToList.
/// Each element is a Dictionary&lt;string, object?&gt; with { segment, index, input, isWordLike? }.
/// </summary>
public class SharpTSIntlSegments : IEnumerable<object?>
{
    private readonly List<object?> _items = [];
    private readonly string _input;
    private readonly string _granularity;

    public int Count => _items.Count;

    public object? this[int index] => _items[index];

    public SharpTSIntlSegments(string input, string granularity)
    {
        _input = input;
        _granularity = granularity;
        Populate();
    }

    public IEnumerator<object?> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    private void Populate()
    {
        switch (_granularity)
        {
            case "grapheme":
                PopulateGraphemes();
                break;
            case "word":
                PopulateWords();
                break;
            case "sentence":
                PopulateSentences();
                break;
        }
    }

    private void PopulateGraphemes()
    {
        var enumerator = StringInfo.GetTextElementEnumerator(_input);
        int index = 0;
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            _items.Add(MakeSegment(element, index));
            index += element.Length;
        }
    }

    private void PopulateWords()
    {
        if (string.IsNullOrEmpty(_input))
            return;

        // Split by word boundaries — alternating word and non-word segments
        var matches = Regex.Matches(_input, @"\w+|\W+");
        int index = 0;
        foreach (Match match in matches)
        {
            string segment = match.Value;
            bool isWordLike = Regex.IsMatch(segment, @"^\w+$");
            _items.Add(MakeWordSegment(segment, index, isWordLike));
            index += segment.Length;
        }
    }

    private void PopulateSentences()
    {
        if (string.IsNullOrEmpty(_input))
            return;

        // Split on sentence boundaries (after sentence-ending punctuation followed by whitespace)
        var parts = Regex.Split(_input, @"(?<=[.!?])\s+");
        int index = 0;
        foreach (string part in parts)
        {
            if (part.Length == 0)
                continue;

            // Find the actual position in the input string
            int pos = _input.IndexOf(part, index, StringComparison.Ordinal);
            if (pos > index)
            {
                index = pos;
            }

            _items.Add(MakeSegment(part, index));
            index += part.Length;

            // Skip any whitespace after sentence boundary that was consumed by the split
            while (index < _input.Length && char.IsWhiteSpace(_input[index]))
                index++;
        }
    }

    private Dictionary<string, object?> MakeSegment(string segment, int index)
    {
        return new Dictionary<string, object?>
        {
            ["segment"] = segment,
            ["index"] = (double)index,
            ["input"] = _input,
        };
    }

    private Dictionary<string, object?> MakeWordSegment(string segment, int index, bool isWordLike)
    {
        return new Dictionary<string, object?>
        {
            ["segment"] = segment,
            ["index"] = (double)index,
            ["input"] = _input,
            ["isWordLike"] = isWordLike,
        };
    }

    /// <summary>
    /// Returns the segment at the given character index position.
    /// </summary>
    public object? FindContaining(int index)
    {
        foreach (var item in _items)
        {
            if (item is IDictionary<string, object?> dict)
            {
                double segIndex = dict.TryGetValue("index", out var idx) && idx is double d ? d : -1;
                string segment = dict.TryGetValue("segment", out var seg) && seg is string s ? s : "";
                if (index >= segIndex && index < segIndex + segment.Length)
                    return dict;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets a member (method) by name for interpreter dispatch.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "containing" => BuiltInMethod.CreateV2("containing", 1, (_, _, args) =>
            {
                int idx = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
                return RuntimeValue.FromBoxed(FindContaining(idx));
            }),
            _ => null
        };
    }

    /// <summary>
    /// JS-facing containing() method for compiled mode reflection dispatch.
    /// </summary>
    public object? containing(object? index)
    {
        int idx = index switch
        {
            double d => (int)d,
            int i => i,
            long l => (int)l,
            _ => 0
        };
        return FindContaining(idx);
    }

    public override string ToString() => "[object Intl.Segments]";
}
