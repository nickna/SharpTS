using System.Globalization;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SharpTS.Tests.Infrastructure;

/// <summary>Lets xUnit discover independent source-file-map rows without losing their data.</summary>
[DataDiscoverer("Xunit.Sdk.MemberDataDiscoverer", "xunit.core")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class SerializableMemberDataAttribute(string memberName, params object[] parameters)
    : MemberDataAttributeBase(memberName, parameters)
{
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        var rows = base.GetData(testMethod);
        return rows is null ? null! : rows.Select(ConvertRow);
    }

    protected override object[] ConvertDataItem(MethodInfo testMethod, object item)
        => item is null ? null! : item as object[]
            ?? throw new ArgumentException($"Member {MemberName} on {MemberType ?? testMethod.DeclaringType} yielded an item that is not an object[].");

    private static object[] ConvertRow(object[] row)
    {
        if (row is null) return null!;
        var maps = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var value in row)
        {
            if (value is Dictionary<string, string> files)
            {
                // Unsupported comparers, subclasses and aliases keep normal delayed-theory behavior.
                if (files.GetType() != typeof(Dictionary<string, string>)
                    || !ReferenceEquals(files.Comparer, EqualityComparer<string>.Default)
                    || !maps.Add(files)) return row;
            }
            else if (!IsScalar(value)) return row;
        }
        if (maps.Count == 0) return row;

        return row.Select(value => value is Dictionary<string, string> files
            ? new SerializableSourceFiles(files) : value).ToArray();
    }

    private static bool IsScalar(object? value)
    {
        if (value is string text)
        {
            // These arguments still use xUnit's string transport. Leave a row
            // opaque if that transport cannot preserve its original code units.
            for (var index = 0; index < text.Length; index++)
            {
                if (char.IsHighSurrogate(text[index]))
                {
                    if (++index >= text.Length || !char.IsLowSurrogate(text[index])) return false;
                }
                else if (char.IsLowSurrogate(text[index])) return false;
            }
            return true;
        }
        if (value is char character) return !char.IsSurrogate(character);
        return value is null or bool or byte or sbyte or short or ushort
            or int or uint or long or ulong or float or double or decimal
            || value.GetType().IsEnum;
    }
}

/// <summary>Test-data transport for default-comparer file maps; it is never a guest value.</summary>
public sealed class SerializableSourceFiles : Dictionary<string, string>, IXunitSerializable
{
    // xUnit creates this empty instance before calling Deserialize.
    public SerializableSourceFiles() { }

    internal SerializableSourceFiles(Dictionary<string, string> files) : base(files, files.Comparer) { }

    public void Serialize(IXunitSerializationInfo info)
    {
        info.AddValue("version", 1);
        info.AddValue("count", Count);
        var index = 0;
        foreach (var (key, value) in this)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            index++;
            // Numeric code units avoid xUnit's UTF-8 string encoding replacing lone surrogates.
            info.AddValue("key" + suffix, Encode(key), typeof(int[]));
            info.AddValue("value" + suffix, Encode(value), typeof(int[]));
        }
    }

    public void Deserialize(IXunitSerializationInfo info)
    {
        if (info.GetValue<int>("version") != 1)
            throw new ArgumentException("Unsupported source-file-map format.");
        var count = info.GetValue<int>("count");
        if (count < 0) throw new ArgumentException("Invalid source-file-map count.");
        var entries = new Dictionary<string, string>();
        for (var index = 0; index < count; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            var key = Decode(info.GetValue<int[]>("key" + suffix))
                ?? throw new ArgumentException("A source file name must not be null.");
            entries.Add(key, Decode(info.GetValue<int[]>("value" + suffix))!);
        }

        // Validate the whole payload before changing an existing instance.
        Clear();
        foreach (var entry in entries) Add(entry.Key, entry.Value);
    }

    private static int[]? Encode(string? value)
        => value?.Select(character => (int)character).ToArray();

    private static string? Decode(int[]? units)
    {
        if (units is null) return null;
        var characters = new char[units.Length];
        for (var index = 0; index < units.Length; index++)
        {
            if ((uint)units[index] > char.MaxValue)
                throw new ArgumentException("Invalid UTF-16 code unit in source-file map.");
            characters[index] = (char)units[index];
        }
        return new string(characters);
    }
}
