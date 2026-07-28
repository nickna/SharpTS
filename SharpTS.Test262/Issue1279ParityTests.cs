using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Test262;

public sealed class Issue1279ParityTests
{
    private readonly ITestOutputHelper _output;

    public Issue1279ParityTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> ObjectPropertyKeyCases => new()
    {
        "built-ins/Object/defineProperty/15.2.3.6-2-39.js",
        "built-ins/Object/defineProperty/15.2.3.6-2-45.js",
        "built-ins/Object/defineProperty/15.2.3.6-2-47.js",
        "built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-2-39.js",
        "built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-2-43.js",
        "built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-2-47.js",
    };

    [Theory]
    [MemberData(nameof(ObjectPropertyKeyCases))]
    public void Object_property_keys_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-2-1.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-2-2.js")]
    public void Missing_object_descriptors_are_undefined_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-14.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-40.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-75.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-100.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-182.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-202.js")]
    public void Built_in_object_descriptors_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> ObjectDescriptorValidationCases => new()
    {
        "built-ins/Object/defineProperty/15.2.3.6-3-1.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-2.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-3.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-4.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-5.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-6.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-7.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-8.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-9.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-10.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-11.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-12.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-13.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-14.js",
    };

    [Theory]
    [MemberData(nameof(ObjectDescriptorValidationCases))]
    public void Invalid_object_descriptors_throw_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-15.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-16.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-17.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-18.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-19.js")]
    public void Descriptor_arguments_must_be_objects_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-33-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-34-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-35-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-38-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-39.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-43-1.js")]
    public void Descriptor_fields_on_exotic_objects_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-41.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-94.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-147.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-173.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-226.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-256.js")]
    public void Descriptor_fields_on_JSON_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-45.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-70.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-98.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-123.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-151.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-177.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-202.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-230.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-260.js")]
    public void Descriptor_fields_on_global_object_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-75.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-77.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-96.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-98.js")]
    public void Same_accessors_can_refine_nonconfigurable_properties_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-126.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-143.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-148.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-161.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-165.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-166.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-171.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-175.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-178.js")]
    public void Array_length_descriptors_shrink_arrays_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-38.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-39.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-41.js")]
    public void Exotic_ordinary_objects_support_descriptors_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    private void AssertPassInBothModes(string relativePath)
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null)
        {
            _output.WriteLine("external/test262 not initialized");
            return;
        }

        var testPath = Path.Combine(Test262Paths.TestDir(root), relativePath);
        Assert.True(File.Exists(testPath), $"Expected Test262 file at {testPath}");

        foreach (var mode in new[]
                 {
                     Test262ExecutionMode.Interpreted,
                     Test262ExecutionMode.Compiled,
                 })
        {
            var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
            var result = runner.RunOne(testPath, mode);

            _output.WriteLine($"{mode} {relativePath} -> {result.Outcome}: {result.Message}");
            Assert.True(
                result.Outcome == Test262Outcome.Pass,
                $"{mode} {relativePath} -> {result.Outcome}: {result.Message}");
        }
    }
}
