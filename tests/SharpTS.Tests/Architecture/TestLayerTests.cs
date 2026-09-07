using System.Reflection;
using System.Text.Json;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.Architecture;

public class TestLayerTests
{
    [Fact]
    public void Every_test_class_has_one_valid_layer_and_no_stale_rules()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), "tests", "test-layers.json")));
        var layers = config.RootElement.GetProperty("layers").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
        var namespaces = config.RootElement.GetProperty("namespaces").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var overrides = config.RootElement.GetProperty("types").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var types = typeof(TestLayerTests).Assembly.GetTypes().Where(t => t.GetMethods().Any(m => m.GetCustomAttributes<FactAttribute>().Any())).ToArray();
        Assert.NotEmpty(types);
        Assert.All(namespaces.Values.Concat(overrides.Values), layer => Assert.Contains(layer, layers));
        Assert.All(overrides.Keys, name => Assert.Contains(types, t => t.FullName == name));
        Assert.All(namespaces.Keys, name => Assert.Contains(types, t => t.FullName!.StartsWith(name + ".", StringComparison.Ordinal)));
        var counts = layers.ToDictionary(l => l, _ => 0);
        foreach (var type in types)
        {
            var rule = overrides.TryGetValue(type.FullName!, out var value) ? value : namespaces
                .Where(p => type.FullName!.StartsWith(p.Key + ".", StringComparison.Ordinal))
                .OrderByDescending(p => p.Key.Length).Select(p => p.Value).FirstOrDefault();
            Assert.True(rule is not null, $"Assign {type.FullName} a test layer in tests/test-layers.json.");
            counts[rule]++;
        }
        Assert.All(counts, count => Assert.True(count.Value > 0, $"Layer {count.Key} is empty."));
    }
}
