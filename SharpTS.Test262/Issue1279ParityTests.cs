using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Test262;

[CollectionDefinition("Issue 1279 parity", DisableParallelization = true)]
public sealed class Issue1279ParityCollection
{
}

[Collection("Issue 1279 parity")]
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

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-33.js")]
    public void Function_objects_enforce_descriptors_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-402.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-405.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-406.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-578.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-582.js")]
    public void Intrinsic_prototypes_support_descriptors_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-191.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-206.js")]
    public void Array_index_descriptors_support_ordinary_properties_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-300-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-531-16.js")]
    public void Arguments_index_accessors_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-463.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-481.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-498.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-516.js")]
    public void Undefined_accessors_remain_own_properties_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-430.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-439.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-448.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-457.js")]
    public void Undefined_accessor_descriptors_keep_their_kind_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-34.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-43.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-184.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-185.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-186.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-339-2.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-339-3.js")]
    public void Array_named_properties_follow_ordinary_descriptor_rules_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-598.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-599.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-600.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-601.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-602.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-603.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-604.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-605.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-606.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-607.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-608.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-609.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-610.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-611.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-612.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-613.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-614.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-615.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-616.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-617.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-618.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-619.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-620.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-621.js")]
    public void Built_in_method_descriptors_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/name.js")]
    [InlineData("built-ins/Object/defineProperty/not-a-constructor.js")]
    public void Built_in_function_metadata_matches_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void RegExp_prototype_function_metadata_is_isolated_between_realms()
    {
        const string relativePath =
            "built-ins/RegExp/prototype/test/S15.10.6.3_A9.js";
        AssertPass(relativePath, Test262ExecutionMode.Interpreted);
        AssertPass(relativePath, Test262ExecutionMode.Interpreted);
    }

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-390.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-417.js")]
    public void Callable_descriptor_values_preserve_identity_and_inheritance_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void RegExp_expando_descriptors_match_in_both_modes()
        => AssertPassInBothModes("built-ins/Object/defineProperty/15.2.3.6-4-40.js");

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-150.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-151.js")]
    public void Array_length_descriptor_values_use_ToNumber_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-1.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-1-3.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-1-4.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-3.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-5.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-7.js")]
    public void DefineProperties_handles_primitive_boundaries_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-11.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-12.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-13.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-14.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-7.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-8.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-12.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-13.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-14.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-15.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-17.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-b-239.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-b-240.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-b-244.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-b-245.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-b-246.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-b-247.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-b-249.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-20.js")]
    public void DefineProperties_supports_object_carriers_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-1-9.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-1-11.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-1-12.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-1-13.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-1-14.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-1-11.js")]
    [InlineData("built-ins/Array/prototype/filter/15.4.4.20-1-13.js")]
    [InlineData("built-ins/Array/prototype/some/15.4.4.17-1-14.js")]
    [InlineData("built-ins/Array/prototype/every/15.4.4.16-1-9.js")]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-1-12.js")]
    public void Array_prototype_methods_support_generic_receivers_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/every/15.4.4.16-7-c-ii-8.js")]
    [InlineData("built-ins/Array/prototype/every/15.4.4.16-7-c-ii-16.js")]
    [InlineData("built-ins/Array/prototype/filter/15.4.4.20-9-c-ii-6.js")]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-7-c-ii-20.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-8-c-ii-8.js")]
    [InlineData("built-ins/Array/prototype/some/15.4.4.17-7-c-ii-17.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-9-c-ii-8.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-9-b-4.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-9-b-8.js")]
    public void Array_callback_methods_observe_live_generic_receivers_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> ArrayIndexSearchCases => new()
    {
        "built-ins/Array/prototype/indexOf/15.4.4.14-2-17.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-5-19.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-5-23.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-5-24.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-5-25.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-5-26.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-5-27.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-9-10.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-9-a-2.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-9-a-3.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-9-a-5.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-9-a-6.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-9-b-ii-4.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-9-b-ii-5.js",
        "built-ins/Array/prototype/indexOf/call-with-boolean.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-1-5.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-2-17.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-19.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-21.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-22.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-23.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-24.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-25.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-26.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-27.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-10.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-11.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-13.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-15.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-2.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-3.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-7.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-b-i-29.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-b-ii-4.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-b-ii-5.js",
        "built-ins/Array/prototype/lastIndexOf/call-with-boolean.js",
    };

    [Theory]
    [MemberData(nameof(ArrayIndexSearchCases))]
    public void Array_index_searches_observe_live_properties_and_coercion_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/indexOf/15.4.4.14-2-4.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-2-4.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-12.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-5-16.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-9.js")]
    [InlineData("built-ins/Array/prototype/every/15.4.4.16-7-6.js")]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-7-5.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-8-6.js")]
    [InlineData("built-ins/Array/prototype/some/15.4.4.17-7-6.js")]
    [InlineData("built-ins/Object/preventExtensions/15.2.3.10-3-11.js")]
    [InlineData("built-ins/Object/preventExtensions/15.2.3.10-3-4.js")]
    public void Interpreted_array_traversal_preserves_length_prototypes_and_sparse_indices(
        string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/every/15.4.4.16-4-1.js")]
    [InlineData("built-ins/Array/prototype/some/15.4.4.17-4-1.js")]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-4-1.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-4-1.js")]
    [InlineData("built-ins/Array/prototype/filter/15.4.4.20-4-1.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-4-1.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-4-1.js")]
    public void Array_callback_methods_throw_TypeError_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/isArray/15.4.3.2-1-13.js")]
    [InlineData("built-ins/Array/prototype/every/15.4.4.16-1-15.js")]
    [InlineData("built-ins/Array/prototype/some/15.4.4.17-1-15.js")]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-1-15.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-1-15.js")]
    [InlineData("built-ins/Array/prototype/filter/15.4.4.20-1-15.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-1-15.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-1-15.js")]
    [InlineData("built-ins/Object/prototype/toString/Object.prototype.toString.call-arguments.js")]
    public void Arguments_objects_have_distinct_identity_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/charAt/S15.5.4.4_A1.1.js")]
    [InlineData("built-ins/String/prototype/charAt/pos-coerce-string.js")]
    [InlineData("built-ins/String/prototype/charCodeAt/S15.5.4.5_A1.1.js")]
    [InlineData("built-ins/String/prototype/codePointAt/return-abrupt-from-object-pos-to-integer.js")]
    [InlineData("built-ins/String/prototype/indexOf/searchstring-tostring.js")]
    [InlineData("built-ins/String/prototype/lastIndexOf/S15.5.4.8_A4_T3.js")]
    [InlineData("built-ins/String/prototype/includes/coerced-values-of-position.js")]
    [InlineData("built-ins/String/prototype/startsWith/searchstring-found-with-position.js")]
    [InlineData("built-ins/String/prototype/endsWith/coerced-values-of-position.js")]
    [InlineData("built-ins/String/prototype/endsWith/searchstring-is-regexp-throws.js")]
    [InlineData("built-ins/String/prototype/slice/S15.5.4.13_A1_T1.js")]
    [InlineData("built-ins/String/prototype/substring/S15.5.4.15_A2_T4.js")]
    public void String_character_and_search_methods_coerce_arguments_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/fromCharCode/touint16-tonumber-throws-valueof.js")]
    [InlineData("built-ins/String/fromCodePoint/to-number-conversions.js")]
    [InlineData("built-ins/String/fromCodePoint/number-is-out-of-range.js")]
    [InlineData("built-ins/String/prototype/concat/S15.5.4.6_A1_T1.js")]
    [InlineData("built-ins/String/prototype/concat/S15.5.4.6_A1_T2.js")]
    [InlineData("built-ins/String/prototype/localeCompare/15.5.4.9_3.js")]
    [InlineData("built-ins/String/prototype/repeat/count-coerced-to-zero-returns-empty-string.js")]
    [InlineData("built-ins/String/prototype/repeat/count-less-than-zero-throws.js")]
    [InlineData("built-ins/String/prototype/padStart/fill-string-non-strings.js")]
    [InlineData("built-ins/String/prototype/padEnd/normal-operation.js")]
    [InlineData("built-ins/String/prototype/normalize/form-is-not-valid-throws.js")]
    public void Remaining_string_methods_coerce_arguments_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/isExtensible/15.2.3.13-2-21.js")]
    [InlineData("built-ins/Object/isFrozen/15.2.3.12-2-a-13.js")]
    [InlineData("built-ins/Object/isFrozen/15.2.3.12-3-10.js")]
    [InlineData("built-ins/Object/isSealed/15.2.3.11-4-19.js")]
    [InlineData("built-ins/Object/seal/object-seal-o-is-a-function-object.js")]
    public void Built_in_objects_report_integrity_state_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-a-1.js")]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-a-3.js")]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-a-4.js")]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-b-i-1.js")]
    [InlineData("built-ins/Object/seal/object-seal-p-is-own-data-property.js")]
    [InlineData("built-ins/Object/seal/object-seal-p-is-own-accessor-property.js")]
    public void Ordinary_object_integrity_levels_update_property_descriptors_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-a-7.js")]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-a-10.js")]
    [InlineData("built-ins/Object/seal/object-seal-p-is-own-property-of-an-arguments-object-which-implements-its-own-get-own-property.js")]
    [InlineData("built-ins/Object/seal/object-seal-p-is-own-property-of-an-array-object-that-uses-object-s-get-own-property.js")]
    public void Array_like_named_properties_apply_integrity_levels_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-a-9.js")]
    [InlineData("built-ins/Object/seal/object-seal-p-is-own-property-of-a-date-object-that-uses-object-s-get-own-property.js")]
    [InlineData("built-ins/Object/seal/object-seal-p-is-own-property-of-a-function-object-that-uses-object-s-get-own-property.js")]
    [InlineData("built-ins/Object/seal/object-seal-p-is-own-property-of-a-reg-exp-object-that-uses-object-s-get-own-property.js")]
    public void Exotic_object_expandos_apply_integrity_levels_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/seal/seal-arraybuffer.js")]
    [InlineData("built-ins/Object/seal/seal-int8array.js")]
    [InlineData("built-ins/Object/seal/seal-float64array.js")]
    [InlineData("built-ins/Object/seal/seal-bigint64array.js")]
    public void Zero_length_buffer_views_can_be_sealed_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/split/call-split-l-instance-is-string-hello.js")]
    [InlineData("built-ins/String/prototype/split/argument-is-undefined-and-instance-is-string.js")]
    [InlineData("built-ins/String/prototype/split/call-split-null-instance-is-thisnullisnullanullstringnullobject.js")]
    [InlineData("built-ins/String/prototype/split/separator-override-tostring-limit-override-valueof.js")]
    public void String_split_coerces_ordinary_separator_and_limit_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/split/argument-is-new-reg-exp-and-instance-is-string-hello.js")]
    [InlineData("built-ins/String/prototype/split/arguments-are-new-reg-exp-and-3-and-instance-is-string-hello.js")]
    [InlineData("built-ins/String/prototype/split/call-split-new-reg-exp.js")]
    public void String_split_trims_empty_matches_from_constructed_RegExp_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/split/name.js")]
    [InlineData("built-ins/String/prototype/split/checking-if-deleting-the-string-prototype-split-length-property-fails.js")]
    public void String_split_function_metadata_matches_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void String_split_function_metadata_is_isolated_between_realms()
    {
        const string relativePath = "built-ins/String/prototype/split/name.js";
        AssertPass(relativePath, Test262ExecutionMode.Interpreted);
        AssertPass(relativePath, Test262ExecutionMode.Interpreted);
    }

    [Fact]
    public void String_split_coerces_separator_before_returning_for_zero_limit()
        => AssertPassInBothModes(
            "built-ins/String/prototype/split/separator-tostring-error.js");

    [Theory]
    [InlineData("built-ins/String/S15.5.2.1_A1_T7.js")]
    [InlineData("built-ins/String/S15.5.2.1_A1_T9.js")]
    [InlineData("built-ins/String/prototype/indexOf/S15.5.4.7_A1_T9.js")]
    [InlineData("built-ins/String/prototype/split/separator-override-valueof.js")]
    public void Boxed_string_construction_coerces_objects_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/replace/cstm-replace-on-boolean-primitive.js")]
    [InlineData("built-ins/String/prototype/split/cstm-split-on-boolean-primitive.js")]
    public void Boolean_prototype_accepts_symbol_descriptors_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-on-boolean-primitive.js")]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-on-number-primitive.js")]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-on-string-primitive.js")]
    [InlineData("built-ins/String/prototype/match/S15.5.4.10_A1_T10.js")]
    [InlineData("built-ins/String/prototype/match/S15.5.4.10_A2_T1.js")]
    [InlineData("built-ins/String/prototype/search/cstm-search-on-boolean-primitive.js")]
    [InlineData("built-ins/String/prototype/search/S15.5.4.12_A1_T10.js")]
    [InlineData("built-ins/String/prototype/search/S15.5.4.12_A1_T4.js")]
    public void String_match_and_search_coerce_non_RegExp_arguments_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/match/S15.5.4.10_A2_T6.js")]
    [InlineData("built-ins/String/prototype/match/S15.5.4.10_A2_T10.js")]
    public void Unmatched_RegExp_captures_are_undefined_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/RegExp/S15.10.2.8_A3_T25.js")]
    [InlineData("built-ins/RegExp/prototype/exec/S15.10.6.2_A1_T2.js")]
    [InlineData("built-ins/RegExp/prototype/exec/S15.10.6.2_A1_T11.js")]
    [InlineData("built-ins/RegExp/prototype/exec/S15.10.6.2_A1_T16.js")]
    [InlineData("built-ins/RegExp/prototype/exec/S15.10.6.2_A4_T10.js")]
    [InlineData("built-ins/RegExp/prototype/exec/S15.10.6.2_A4_T11.js")]
    [InlineData("built-ins/RegExp/prototype/exec/S15.10.6.2_A7.js")]
    [InlineData("built-ins/RegExp/prototype/exec/failure-g-lastindex-reset.js")]
    [InlineData("built-ins/RegExp/prototype/exec/success-lastindex-access.js")]
    [InlineData("built-ins/RegExp/prototype/exec/y-fail-lastindex-no-write.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/builtin-success-g-set-lastindex-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.search/set-lastindex-init-samevalue.js")]
    public void RegExp_builtin_exec_observes_coercion_captures_and_lastIndex_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void RegExp_lastIndex_is_an_own_data_property_in_interpreted_mode()
        => AssertPass("built-ins/RegExp/lastIndex.js", Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Number/S15.7.5_A1_T01.js")]
    [InlineData("built-ins/Number/S15.7.5_A1_T03.js")]
    [InlineData("built-ins/Object/create/15.2.3.5-4-41.js")]
    [InlineData("built-ins/Object/prototype/toLocaleString/S15.2.4.3_A12.js")]
    [InlineData("built-ins/Object/prototype/toLocaleString/S15.2.4.3_A13.js")]
    [InlineData("built-ins/String/prototype/replace/S15.5.4.11_A1_T16.js")]
    public void Full_baseline_regressions_pass_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-8.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-16.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-9.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A3_T1.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A3_T2.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A3_T3.js")]
    public void Full_interpreted_baseline_regressions_pass(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    // ---- Batch: interpreter property-resolution + built-in-function parity ----

    /// <summary>
    /// A built-in <c>Object.prototype</c> method stored on an object and then invoked as a
    /// member call gets <c>this</c> from the call's Reference Record. The Sputnik suite leans
    /// on this constantly via <c>arr.getClass = Object.prototype.toString</c>.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A1.1_T1.js")]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A1.1_T1.js")]
    [InlineData("built-ins/Array/prototype/concat/S15.4.4.4_A1_T1.js")]
    public void Unbound_prototype_methods_take_receiver_from_member_call(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// Built-in prototype objects and constructors inherit <c>Object.prototype</c>, so
    /// <c>Array.prototype.isPrototypeOf([])</c> and <c>Array.prototype.hasOwnProperty(…)</c>
    /// resolve rather than reporting <c>undefined</c>.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Array/S15.4.1_A1.1_T3.js")]
    [InlineData("built-ins/Array/S15.4.2.1_A1.1_T3.js")]
    [InlineData("built-ins/Array/S15.4.3_A1.1_T3.js")]
    public void Built_in_prototypes_inherit_Object_prototype(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// ECMA-262 §17: a built-in function's <c>name</c>/<c>length</c> are own, configurable
    /// data properties — so <c>propertyHelper.js</c> can delete them to prove configurability.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Array/prototype/concat/length.js")]
    [InlineData("built-ins/Array/prototype/concat/name.js")]
    [InlineData("built-ins/Array/prototype/map/length.js")]
    [InlineData("built-ins/Array/prototype/map/name.js")]
    [InlineData("built-ins/Number/prototype/toFixed/length.js")]
    [InlineData("built-ins/Number/prototype/toFixed/name.js")]
    public void Built_in_function_name_and_length_are_configurable_own_properties(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// The primitive prototype objects carry their own primitive data slot
    /// (<c>Number.prototype</c> is +0, <c>Boolean.prototype</c> is false,
    /// <c>String.prototype</c> is ""), so their prototype methods work on them directly.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/prototype/toString/S15.7.4.2_A1_T01.js")]
    [InlineData("built-ins/Number/prototype/S15.7.3.1_A2_T1.js")]
    public void Primitive_prototypes_carry_their_own_primitive_value(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// Math functions the compiled backend has always emitted but the interpreter reported
    /// as <c>undefined</c>.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Math/acosh/length.js")]
    [InlineData("built-ins/Math/clz32/length.js")]
    [InlineData("built-ins/Math/fround/length.js")]
    [InlineData("built-ins/Math/imul/length.js")]
    [InlineData("built-ins/Math/log1p/length.js")]
    public void Missing_Math_functions_exist_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>Annex B §B.2.2.2–5 accessor helpers on <c>Object.prototype</c>.</summary>
    [Theory]
    [InlineData("built-ins/Object/prototype/__defineGetter__/length.js")]
    [InlineData("built-ins/Object/prototype/__defineSetter__/length.js")]
    [InlineData("built-ins/Object/prototype/__lookupGetter__/length.js")]
    [InlineData("built-ins/Object/prototype/__lookupSetter__/length.js")]
    public void Annex_B_accessor_helpers_exist_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// Built-ins run ToNumber on their numeric arguments: strings coerce, Symbols raise a
    /// guest TypeError. Previously the interpreter hard-failed with a host
    /// "RuntimeValue has Kind …" message that reached guest <c>catch</c> as a bare string.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/prototype/toPrecision/return-abrupt-tointeger-precision-symbol.js")]
    [InlineData("built-ins/Number/prototype/toFixed/toFixed-tonumber-throws-typeerror-symbol.js")]
    [InlineData("built-ins/Array/prototype/at/index-non-numeric-argument-tointeger-invalid.js")]
    public void Numeric_built_in_arguments_are_ToNumber_coerced(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>ECMA-262 §25.5.1: malformed JSON text is a guest <c>SyntaxError</c> object.</summary>
    [Theory]
    [InlineData("built-ins/JSON/parse/15.12.1.1-0-1.js")]
    [InlineData("built-ins/JSON/parse/15.12.1.1-0-2.js")]
    [InlineData("built-ins/JSON/parse/15.12.1.1-0-3.js")]
    public void JSON_parse_throws_a_SyntaxError_object(string relativePath)
        => AssertPassInBothModes(relativePath);

    // ---- Batch: prototype identity, Promise.prototype, iterable combinators ----

    /// <summary>
    /// The Promise combinators take any iterable (ECMA-262 §27.2.4.1 step 3 GetIterator), and
    /// an abrupt completion there *rejects* the returned promise rather than throwing
    /// synchronously (IfAbruptRejectPromise). They previously demanded a literal array.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Promise/all/iter-arg-is-null-reject.js")]
    [InlineData("built-ins/Promise/all/iter-arg-is-number-reject.js")]
    [InlineData("built-ins/Promise/race/iter-arg-is-null-reject.js")]
    [InlineData("built-ins/Promise/allSettled/iter-arg-is-null-reject.js")]
    [InlineData("built-ins/Promise/any/iter-arg-is-null-reject.js")]
    public void Promise_combinators_reject_on_non_iterable(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// <c>Promise.prototype</c> is a real object carrying the unbound reaction methods; it
    /// read as <c>undefined</c>, so every access through it threw.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Promise/prototype/finally/is-a-function.js")]
    [InlineData("built-ins/Promise/prototype/catch/length.js")]
    [InlineData("built-ins/Promise/prototype/then/length.js")]
    [InlineData("built-ins/Promise/prototype/then/context-check-on-entry.js")]
    [InlineData("built-ins/Promise/prototype/catch/this-value-non-object.js")]
    public void Promise_prototype_is_an_object_with_unbound_methods(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// A constructor has exactly one <c>prototype</c> object, and an instance's
    /// [[Prototype]] is that same object — <c>X.prototype === X.prototype</c> and
    /// <c>Object.getPrototypeOf(new X()) === X.prototype</c>. A plain object literal
    /// likewise reports Object.prototype, not null.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-2-1.js")]
    public void Prototype_objects_are_identity_stable(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// §10.2.5: a derived constructor's [[Prototype]] is its base constructor, so
    /// <c>Object.getPrototypeOf(RangeError) === Error</c>. Interpreted-only: the compiled
    /// path answers with a raw Dictionary here (a Track B item on #1279).
    /// </summary>
    [Theory]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-2-13.js")]
    public void Derived_constructors_inherit_their_base_constructor(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    /// <summary>
    /// <c>Object.hasOwn</c> is defined as HasOwnProperty (§20.1.2.13), so it must see
    /// accessor properties — and must NOT see inherited class methods.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Object/hasOwn/hasown_own_getter.js")]
    [InlineData("built-ins/Object/hasOwn/hasown_own_getter_and_setter.js")]
    [InlineData("built-ins/Object/hasOwn/hasown.js")]
    public void Object_hasOwn_matches_hasOwnProperty(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>ECMA-262 §9.4.2: resolving an unbound name throws a ReferenceError.</summary>
    [Theory]
    [InlineData("built-ins/Array/prototype/filter/15.4.4.20-4-2.js")]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-4-2.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-4-2.js")]
    public void Unresolvable_names_throw_ReferenceError(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// §10.1.10: a <c>configurable: false</c> own property of a class instance cannot be
    /// deleted — the check propertyHelper.js uses to prove non-configurability.
    /// Interpreted-only: the compiled path still reports this property as writable
    /// (a Track B item on #1279).
    /// </summary>
    [Theory]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-21.js")]
    public void Non_configurable_instance_properties_resist_delete(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    // ---- Batch: Track B — compiled-mode deficits ----

    /// <summary>
    /// <c>Date.prototype</c> is addressable as a value carrying its §21.4.4 method table.
    /// The compiled backend emitted Date instance calls inline and never materialized the
    /// prototype object, so it read as <c>undefined</c> and every reflective use of it threw.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-116.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-117.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-130.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-150.js")]
    public void Date_prototype_is_addressable_as_a_value(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// §20.1.2.3.1 ObjectDefineProperties step 4 does <c>Get(props, key)</c>, so an accessor
    /// property on the descriptor bag has its getter invoked. The compiled <c>Object.create</c>
    /// walked the backing dictionary directly and silently dropped such entries; it now
    /// delegates to ObjectDefineProperties, which is that step.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Object/create/15.2.3.5-4-19.js")]
    [InlineData("built-ins/Object/create/15.2.3.5-4-22.js")]
    [InlineData("built-ins/Object/create/15.2.3.5-4-23.js")]
    [InlineData("built-ins/Object/create/15.2.3.5-4-17.js")]
    public void Object_create_invokes_getters_on_the_descriptor_bag(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// §10.4.2.1 routes an array-index [[DefineOwnProperty]] through
    /// OrdinaryDefineOwnProperty, so an index can carry an accessor descriptor. The compiled
    /// index read went straight to element storage and answered <c>undefined</c>.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-221.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-244.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-245.js")]
    public void Array_indices_support_accessor_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    // ---- Batch: Object.prototype as an ordinary object ----

    /// <summary>
    /// ECMA-262 makes every built-in prototype an ordinary object, so guest code can define
    /// descriptors on it, index into it, delete from it, and enumerate it. Object.prototype
    /// alone was backed by a value-only dictionary and supported none of that — Test262
    /// patches it constantly to exercise inherited-property paths.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Array/prototype/every/15.4.4.16-2-12.js")]
    [InlineData("built-ins/Array/prototype/filter/15.4.4.20-2-12.js")]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-2-12.js")]
    [InlineData("built-ins/Array/prototype/every/15.4.4.16-7-b-10.js")]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-7-b-10.js")]
    public void Object_prototype_is_an_ordinary_mutable_object(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// <c>for...in</c> over a built-in prototype singleton yields its own enumerable keys
    /// rather than throwing "for...in requires an object".
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/prototype/toFixed/prop-desc.js")]
    [InlineData("built-ins/Number/prototype/toExponential/prop-desc.js")]
    [InlineData("built-ins/Number/prototype/constructor.js")]
    public void For_in_enumerates_built_in_prototypes(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// A class's <c>prototype</c> accepts descriptor definitions, including Symbol-keyed ones
    /// (<c>Object.defineProperty(Error.prototype, Symbol.toStringTag, …)</c>).
    /// </summary>
    [Theory]
    [InlineData("built-ins/Error/prototype/no-error-data.js")]
    public void Class_prototypes_accept_descriptor_definitions(string relativePath)
        => AssertPassInBothModes(relativePath);

    // ---- Batch: per-realm constructor objects ----

    /// <summary>
    /// ECMA-262 makes the <c>Number</c>/<c>String</c>/<c>Boolean</c> constructor objects
    /// ordinary and extensible, and their statics non-writable. Assigning to a static is a
    /// silent no-op in sloppy mode — it threw "Index assignment not supported" before.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/MAX_VALUE/S15.7.3.2_A2.js")]
    [InlineData("built-ins/Number/MIN_VALUE/S15.7.3.3_A2.js")]
    [InlineData("built-ins/Number/NEGATIVE_INFINITY/S15.7.3.5_A2.js")]
    [InlineData("built-ins/Number/POSITIVE_INFINITY/S15.7.3.6_A2.js")]
    public void Constructor_object_statics_are_read_only(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// A constructor object read twice yields the same value, and matches the <c>value</c> of
    /// its own descriptor — routing a static through instance-member dispatch would hand out
    /// a freshly bound copy per read.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-61.js")]
    public void Constructor_object_statics_keep_their_identity(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// ECMA-262 §17 makes a built-in function's <c>length</c>/<c>name</c> configurable, and
    /// propertyHelper.js proves that by deleting them. That deletion must not outlive the
    /// program: these methods used to be handed out as process-wide singletons, so one
    /// program's delete was visible to the next one sharing the process — making results
    /// order-dependent.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Object/prototype/toString/length.js")]
    [InlineData("built-ins/Object/prototype/toString/name.js")]
    [InlineData("built-ins/Object/prototype/hasOwnProperty/length.js")]
    public void Built_in_metadata_deletion_does_not_outlive_the_program(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// Issue #1326: Error constructors used to live in the process-static globals table.
    /// A test that replaced Error.prototype.toString therefore changed the callable and
    /// descriptor observed by later Interpreter instances in the same persistent worker.
    /// </summary>
    [Fact]
    public void Error_prototype_mutation_does_not_outlive_the_program()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null)
        {
            _output.WriteLine("external/test262 not initialized");
            return;
        }

        var testDir = Test262Paths.TestDir(root);
        var runner = new Test262Runner(
            root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        string[] relativePaths =
        [
            "built-ins/Error/prototype/S15.11.4_A2.js",
            "built-ins/Error/prototype/toString/length.js",
            "built-ins/Error/prototype/toString/name.js",
            "built-ins/Error/prototype/toString/not-a-constructor.js",
            "built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-169.js",
        ];

        foreach (var relativePath in relativePaths)
        {
            var result = runner.RunOne(
                Path.Combine(testDir, relativePath), Test262ExecutionMode.Interpreted);
            _output.WriteLine($"Interpreted {relativePath} -> {result.Outcome}: {result.Message}");
            Assert.True(
                result.Outcome == Test262Outcome.Pass,
                $"Interpreted {relativePath} -> {result.Outcome}: {result.Message}");
        }
    }

    private void AssertPassInBothModes(string relativePath)
    {
        foreach (var mode in new[]
                 {
                     Test262ExecutionMode.Interpreted,
                     Test262ExecutionMode.Compiled,
                 })
        {
            AssertPass(relativePath, mode);
        }
    }

    [Theory]
    [InlineData("built-ins/String/prototype/trim/15.5.4.20-2-51.js")]
    [InlineData("built-ins/String/prototype/trim/15.5.4.20-3-2.js")]
    [InlineData("built-ins/String/prototype/trim/15.5.4.20-3-3.js")]
    [InlineData("built-ins/String/prototype/trim/15.5.4.20-3-4.js")]
    [InlineData("built-ins/String/prototype/trim/15.5.4.20-3-5.js")]
    [InlineData("built-ins/String/prototype/trim/15.5.4.20-3-6.js")]
    [InlineData("built-ins/String/prototype/trim/15.5.4.20-4-10.js")]
    [InlineData("built-ins/String/prototype/trim/15.5.4.20-4-18.js")]
    [InlineData("built-ins/String/prototype/trim/15.5.4.20-4-34.js")]
    [InlineData("built-ins/String/prototype/trimStart/this-value-whitespace.js")]
    [InlineData("built-ins/String/prototype/trimStart/this-value-object-toprimitive-call-err.js")]
    [InlineData("built-ins/String/prototype/trimStart/this-value-object-toprimitive-meth-err.js")]
    [InlineData("built-ins/String/prototype/trimStart/this-value-object-toprimitive-meth-priority.js")]
    [InlineData("built-ins/String/prototype/trimStart/this-value-object-toprimitive-returns-object-err.js")]
    [InlineData("built-ins/String/prototype/trimStart/this-value-object-tostring-meth-priority.js")]
    [InlineData("built-ins/String/prototype/trimStart/this-value-object-valueof-meth-priority.js")]
    [InlineData("built-ins/String/prototype/trimEnd/this-value-whitespace.js")]
    [InlineData("built-ins/String/prototype/trimEnd/this-value-object-toprimitive-call-err.js")]
    [InlineData("built-ins/String/prototype/trimEnd/this-value-object-toprimitive-meth-err.js")]
    [InlineData("built-ins/String/prototype/trimEnd/this-value-object-toprimitive-meth-priority.js")]
    [InlineData("built-ins/String/prototype/trimEnd/this-value-object-toprimitive-returns-object-err.js")]
    [InlineData("built-ins/String/prototype/trimEnd/this-value-object-tostring-meth-priority.js")]
    [InlineData("built-ins/String/prototype/trimEnd/this-value-object-valueof-meth-priority.js")]
    public void String_trimming_uses_spec_whitespace_and_ToPrimitive(string relativePath)
        => AssertPassInBothModes(relativePath);

    private void AssertPass(string relativePath, Test262ExecutionMode mode)
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null)
        {
            _output.WriteLine("external/test262 not initialized");
            return;
        }

        var testPath = Path.Combine(Test262Paths.TestDir(root), relativePath);
        Assert.True(File.Exists(testPath), $"Expected Test262 file at {testPath}");

        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        var result = runner.RunOne(testPath, mode);

        _output.WriteLine($"{mode} {relativePath} -> {result.Outcome}: {result.Message}");
        Assert.True(
            result.Outcome == Test262Outcome.Pass,
            $"{mode} {relativePath} -> {result.Outcome}: {result.Message}");
    }
}
