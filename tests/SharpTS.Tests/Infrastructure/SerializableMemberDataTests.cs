using System.Collections;
using System.Reflection;
using SharpTS.Tests.CompilerTests;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SharpTS.Tests.Infrastructure;

public sealed class SerializableMemberDataTests
{
    [Fact]
    public void ExistingFileMapRowsKeepEverySourceExpectedValueFlagAndOrder()
    {
        var methods = typeof(StandaloneDllTests).GetMethods()
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<SerializableMemberDataAttribute>()))
            .Where(item => item.Attribute is not null).ToArray();
        Assert.Equal(15, methods.Length);
        var rows = 0;
        foreach (var (method, attribute) in methods)
        {
            var original = new MemberDataAttribute(attribute!.MemberName, attribute.Parameters)
            {
                MemberType = attribute.MemberType,
                DisableDiscoveryEnumeration = attribute.DisableDiscoveryEnumeration,
                Skip = attribute.Skip
            };
            var before = original.GetData(method).ToArray();
            var after = attribute.GetData(method).ToArray();
            Assert.Equal(before.Length, after.Length);
            for (var index = 0; index < before.Length; index++)
            {
                Assert.Equal(before[index].Length, after[index].Length);
                Assert.True(SerializationHelper.IsSerializable(after[index]));
                var restored = SerializationHelper.Deserialize<object[]>(SerializationHelper.Serialize(after[index]));
                for (var argument = 0; argument < before[index].Length; argument++)
                {
                    if (before[index][argument] is Dictionary<string, string> files)
                    {
                        var transported = Assert.IsType<SerializableSourceFiles>(restored[argument]);
                        Assert.Equal(files.Comparer, transported.Comparer);
                        Assert.Equal(files.ToArray(), transported.ToArray());
                    }
                    else Assert.Equal(before[index][argument], restored[argument]);
                }
                rows++;
            }
        }
        Assert.Equal(223, rows);
    }

    [Fact]
    public void RealXunitSerializationPreservesUtf16NullValuesAndEmptyMaps()
    {
        var original = new Dictionary<string, string>
        {
            [""] = "",
            ["A"] = "\ud800\0\udfff\r\n\u03bb\U0001f600",
            ["a"] = null!,
            ["\ud800"] = "\udfff"
        };
        var transport = Assert.IsType<SerializableSourceFiles>(Convert([original, "expected", true])[0]);
        var restored = SerializationHelper.Deserialize<SerializableSourceFiles>(SerializationHelper.Serialize(transport));
        Assert.Equal(original.ToArray(), restored.ToArray());
        Assert.Equal(original.Comparer, restored.Comparer);
        Assert.Equal(4, restored.Count);

        var empty = Assert.IsType<SerializableSourceFiles>(Convert([new Dictionary<string, string>()])[0]);
        Assert.Empty(SerializationHelper.Deserialize<SerializableSourceFiles>(SerializationHelper.Serialize(empty)));
    }

    [Fact]
    public void UnsupportedComparerSubclassAliasesAndUnknownArgumentsStayOpaque()
    {
        var insensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["A"] = "source" };
        object[] insensitiveRow = [insensitive, "expected"];
        Assert.Same(insensitiveRow, Convert(insensitiveRow));
        Assert.True(insensitive.ContainsKey("a"));

        object[] subclassRow = [new OtherFiles { ["main.ts"] = "source" }];
        Assert.Same(subclassRow, Convert(subclassRow));
        var files = new Dictionary<string, string> { ["main.ts"] = "source" };
        object[] aliases = [files, files];
        Assert.Same(aliases, Convert(aliases));
        var marker = new object();
        object[] unknown = [files, marker];
        Assert.Same(unknown, Convert(unknown));
        Assert.Same(marker, unknown[1]);
        object[] unpairedScalar = [files, "\ud800"];
        Assert.Same(unpairedScalar, Convert(unpairedScalar));
        object[] unpairedCharacter = [files, '\udfff'];
        Assert.Same(unpairedCharacter, Convert(unpairedCharacter));
    }

    [Fact]
    public void AdapterCallsTheSelectedFactoryOnceAndEnumeratesLazily()
    {
        Sources.FactoryCalls = 0;
        Sources.RowsProduced = 0;
        var attribute = new SerializableMemberDataAttribute(nameof(Sources.TrackedRows), "source", 2)
        {
            MemberType = typeof(Sources)
        };
        var rows = attribute.GetData(TargetMethod);
        Assert.Equal(1, Sources.FactoryCalls);
        Assert.Equal(0, Sources.RowsProduced);
        using var iterator = rows.GetEnumerator();
        Assert.True(iterator.MoveNext());
        Assert.Equal(1, Sources.RowsProduced);
        Assert.Equal("source0", Assert.IsType<SerializableSourceFiles>(iterator.Current[0])["main.ts"]);
        Assert.Equal(1, Sources.FactoryCalls);
        Assert.True(iterator.MoveNext());
        Assert.Equal(2, Sources.RowsProduced);
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void AdapterPreservesNullAndMalformedRowContracts()
    {
        var nullData = new SerializableMemberDataAttribute(nameof(Sources.NullData)) { MemberType = typeof(Sources) };
        Assert.Null(nullData.GetData(TargetMethod));
        var nullRow = new SerializableMemberDataAttribute(nameof(Sources.NullRow)) { MemberType = typeof(Sources) };
        Assert.Null(Assert.Single(nullRow.GetData(TargetMethod)));
        var malformed = new SerializableMemberDataAttribute(nameof(Sources.Malformed)) { MemberType = typeof(Sources) };
        Assert.Throws<ArgumentException>(() => malformed.GetData(TargetMethod).ToArray());
    }

    [Fact]
    public void CanonicalDiscovererAndInheritedControlsRemainAvailable()
    {
        var discoverer = Assert.Single(typeof(SerializableMemberDataAttribute).GetCustomAttributesData(),
            attribute => attribute.AttributeType == typeof(DataDiscovererAttribute));
        Assert.Equal("Xunit.Sdk.MemberDataDiscoverer", discoverer.ConstructorArguments[0].Value);
        Assert.Equal("xunit.core", discoverer.ConstructorArguments[1].Value);
        var attribute = new SerializableMemberDataAttribute(nameof(Sources.SuppliedRows))
        {
            MemberType = typeof(Sources), DisableDiscoveryEnumeration = true, Skip = "preserved property"
        };
        Assert.True(attribute.DisableDiscoveryEnumeration);
        Assert.Equal("preserved property", attribute.Skip);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void InvalidPayloadDoesNotChangeExistingEntries(int kind)
    {
        var info = new SerializationData();
        info.Values["version"] = 1;
        info.Values["count"] = 1;
        info.Values["key0"] = new[] { (int)'k' };
        info.Values["value0"] = new[] { (int)'v' };
        switch (kind)
        {
            case 0: info.Values["version"] = 2; break;
            case 1: info.Values["count"] = -1; break;
            case 2: info.Values["key0"] = null; break;
            case 3: info.Values["value0"] = new[] { 65536 }; break;
            case 4: info.Values["key0"] = new[] { -1 }; break;
            case 5: info.Values.Remove("value0"); break;
            case 6:
                info.Values["count"] = 2;
                info.Values["key1"] = new[] { (int)'k' };
                info.Values["value1"] = Array.Empty<int>();
                break;
        }
        var files = new SerializableSourceFiles { ["existing"] = "unchanged" };
        Assert.ThrowsAny<Exception>(() => files.Deserialize(info));
        Assert.Equal(new[] { new KeyValuePair<string, string>("existing", "unchanged") }, files.ToArray());
    }

    // Adapter integration: discovery should report two rows here and one for the
    // explicitly delayed companion. Both methods still execute both rows normally.
    public static IEnumerable<object[]> SampleRows =>
    [
        [new Dictionary<string, string> { ["main.ts"] = "first" }, "first"],
        [new Dictionary<string, string> { ["main.ts"] = "second" }, "second"]
    ];

    [Theory, SerializableMemberData(nameof(SampleRows))]
    public void SerializableRowsAreDiscovered(Dictionary<string, string> files, string expected)
        => Assert.Equal(expected, files["main.ts"]);

    [Theory, SerializableMemberData(nameof(SampleRows), DisableDiscoveryEnumeration = true)]
    public void ExplicitlyDelayedRowsRemainSupported(Dictionary<string, string> files, string expected)
        => Assert.Equal(expected, files["main.ts"]);

    private static MethodInfo TargetMethod => typeof(SerializableMemberDataTests)
        .GetMethod(nameof(Target), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Target() { }

    private static object[] Convert(object[] row)
    {
        Sources.SuppliedRow = row;
        var attribute = new SerializableMemberDataAttribute(nameof(Sources.SuppliedRows)) { MemberType = typeof(Sources) };
        return Assert.Single(attribute.GetData(TargetMethod));
    }

    private sealed class OtherFiles : Dictionary<string, string> { }

    public static class Sources
    {
        public static object[] SuppliedRow = [];
        public static int FactoryCalls;
        public static int RowsProduced;
        public static IEnumerable<object[]> SuppliedRows => [SuppliedRow];
        public static IEnumerable<object[]> NullData => null!;
        public static IEnumerable<object[]> NullRow => [null!];
        public static IEnumerable Malformed => new[] { "not an object array" };

        public static IEnumerable<object[]> TrackedRows(string prefix, int count)
        {
            FactoryCalls++;
            return Produce(prefix, count);
        }

        private static IEnumerable<object[]> Produce(string prefix, int count)
        {
            for (var index = 0; index < count; index++)
            {
                RowsProduced++;
                yield return [new Dictionary<string, string> { ["main.ts"] = prefix + index }];
            }
        }
    }

    private sealed class SerializationData : IXunitSerializationInfo
    {
        public Dictionary<string, object?> Values { get; } = [];
        public void AddValue(string key, object? value, Type? type = null) => Values.Add(key, value);
        public object GetValue(string key, Type type) => Values[key]!;
        public T GetValue<T>(string key) => (T)Values[key]!;
    }
}
