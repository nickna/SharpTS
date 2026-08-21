using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Parser-only attribution probes over the byte-identical payload produced by
/// the cross-runtime JSON workload. The cumulative probes separate the legacy
/// path's UTF-16 validation, UTF-8 transcoding, reader tokenization, and scalar
/// decoding costs from SharpTS object materialization.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class JsonParserSubphaseBenchmarks
{
    private string _json = null!;

    [Params(1000, 10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder(N * 44 + 12);
        builder.Append("{\"items\":[");
        for (int index = 0; index < N; index++)
        {
            if (index != 0)
                builder.Append(',');
            builder.Append("{\"id\":")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .Append(",\"name\":\"item-")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"value\":")
                .Append((index * 3 - 1).ToString(CultureInfo.InvariantCulture))
                .Append('}');
        }
        builder.Append("]}");
        _json = builder.ToString();

        if (N == 10000 && _json.Length != 444086)
        {
            throw new InvalidOperationException(
                $"JSON attribution payload drifted: {_json.Length} characters.");
        }
    }

    [Benchmark(Baseline = true)]
    public int ValidateUtf16()
    {
        bool inString = false;
        bool afterEscape = false;
        for (int index = 0; index < _json.Length; index++)
        {
            char c = _json[index];
            if (afterEscape)
            {
                afterEscape = false;
                continue;
            }
            if (inString && c == '\\')
            {
                afterEscape = true;
                continue;
            }
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            if (c < 0x20 && (inString || c is not ('\t' or '\n' or '\r')))
                throw new JsonException("Unescaped JSON control character.");
        }
        return _json.Length;
    }

    [Benchmark]
    public int ValidateAndTranscodeUtf8()
    {
        ValidateUtf16();
        return WithUtf8(static (buffer, count) => count);
    }

    [Benchmark]
    public int ValidateTranscodeAndTokenize()
    {
        ValidateUtf16();
        return WithUtf8(static (buffer, count) =>
        {
            var reader = new Utf8JsonReader(buffer.AsSpan(0, count));
            int tokens = 0;
            while (reader.Read())
                tokens++;
            return tokens;
        });
    }

    [Benchmark]
    public int ValidateTranscodeTokenizeAndDecodeScalars()
    {
        ValidateUtf16();
        return WithUtf8(static (buffer, count) =>
        {
            var reader = new Utf8JsonReader(buffer.AsSpan(0, count));
            int checksum = 0;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.Number:
                        checksum ^= (int)reader.GetDouble();
                        break;
                    case JsonTokenType.String:
                        checksum ^= reader.GetString()!.Length;
                        break;
                }
            }
            return checksum;
        });
    }

    private int WithUtf8(Func<byte[], int, int> action)
    {
        Encoding encoding = Encoding.UTF8;
        int byteCount = encoding.GetByteCount(_json);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(byteCount, 1));
        try
        {
            encoding.GetBytes(_json, 0, _json.Length, buffer, 0);
            return action(buffer, byteCount);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
