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

    [Theory]
    [InlineData("language/expressions/property-accessors/S11.2.1_A3_T1.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A3_T2.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A3_T3.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A3_T4.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A4_T1.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A4_T2.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A4_T7.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A4_T8.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A4_T9.js")]
    public void Computed_and_global_property_access_matches_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

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

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-163.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-165.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-166.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-167.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-212.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-213.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-214.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-215.js")]
    [InlineData("built-ins/RegExp/prototype/dotAll/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/flags/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/global/15.10.7.2-2.js")]
    [InlineData("built-ins/RegExp/prototype/global/S15.10.7.2_A9.js")]
    [InlineData("built-ins/RegExp/prototype/ignoreCase/15.10.7.3-2.js")]
    [InlineData("built-ins/RegExp/prototype/ignoreCase/S15.10.7.3_A9.js")]
    [InlineData("built-ins/RegExp/prototype/multiline/15.10.7.4-2.js")]
    [InlineData("built-ins/RegExp/prototype/multiline/S15.10.7.4_A9.js")]
    [InlineData("built-ins/RegExp/prototype/source/prop-desc.js")]
    public void RegExp_prototype_descriptors_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-114.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-114-b.js")]
    [InlineData("built-ins/RegExp/S15.10.5_A2_T2.js")]
    public void Intrinsic_objects_observe_ordinary_inherited_and_exotic_properties(string relativePath)
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
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-304.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-305.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-306.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-307.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-308.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-333-8.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-333-10.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-339-4.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-354-16.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-294.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-295.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-296.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-297.js")]
    public void Compiled_arguments_index_descriptors_remain_observable(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

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
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-190.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-214.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-299.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-354-6.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-195.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-283.js")]
    public void Legacy_array_and_arguments_descriptors_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-205.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-216.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-235.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-242.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-261.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-290.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-293-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-354-2.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-538-1.js")]
    public void Configurable_array_and_arguments_descriptors_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-278.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-314.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-315.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-324.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-354-3.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-538-2.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-540-5.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-547-3.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-13.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-267.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-304.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-313.js")]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-a-7.js")]
    [InlineData("built-ins/Object/seal/object-seal-p-is-own-property-of-an-arguments-object-which-implements-its-own-get-own-property.js")]
    public void Arguments_named_accessors_preserve_descriptor_kind_in_both_modes(
        string relativePath)
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
    [InlineData("built-ins/Array/prototype/indexOf/15.4.4.14-9-a-17.js")]
    [InlineData("built-ins/Array/prototype/indexOf/15.4.4.14-9-a-18.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-17.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-18.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-b-i-31.js")]
    public void Array_index_searches_preserve_live_receiver_reads(string relativePath)
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

    [Theory]
    [InlineData("built-ins/Promise/all/ctx-non-object.js")]
    [InlineData("built-ins/Promise/allSettled/ctx-non-object.js")]
    [InlineData("built-ins/Promise/any/ctx-non-object.js")]
    [InlineData("built-ins/Promise/race/ctx-non-object.js")]
    [InlineData("built-ins/Promise/reject/ctx-non-object.js")]
    [InlineData("built-ins/Promise/resolve/context-non-object-with-promise.js")]
    [InlineData("built-ins/Promise/resolve/ctx-non-object.js")]
    public void Promise_static_methods_reject_primitive_receivers(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/ctx-non-ctor.js")]
    [InlineData("built-ins/Promise/allSettled/ctx-non-ctor.js")]
    [InlineData("built-ins/Promise/any/ctx-non-ctor.js")]
    [InlineData("built-ins/Promise/race/ctx-non-ctor.js")]
    public void Promise_combinators_reject_callable_nonconstructors(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

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
    [InlineData("language/expressions/new/spread-mult-empty.js")]
    [InlineData("language/expressions/new/spread-err-mult-err-iter-get-value.js")]
    [InlineData("language/expressions/new/spread-err-sngl-err-itr-get-value.js")]
    public void New_function_expression_spreads_pass_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-146-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-148-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-172-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-174-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-225-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-227-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-255-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-257-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-40-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-42-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-93-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-3-95-1.js")]
    public void Descriptor_objects_read_inherited_intrinsic_properties_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-8-b-iii-1-10.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-8-b-iii-1-6.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-8-b-iii-1-19.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-8-b-iii-1-6.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-9-b-24.js")]
    [InlineData("built-ins/Array/prototype/every/15.4.4.16-2-17.js")]
    [InlineData("built-ins/Array/prototype/some/15.4.4.17-2-17.js")]
    public void Array_iteration_observes_index_descriptors_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/RegExp/dotall/with-dotall.js")]
    [InlineData("built-ins/RegExp/CharacterClassEscapes/character-class-digit-class-escape-positive-cases.js")]
    public void Iterator_protocol_changes_preserve_regexp_cases(string relativePath)
        => AssertPassInBothModes(relativePath);

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

    /// <summary>
    /// Number predicates treat a missing/non-number argument as false while retaining a
    /// spec-visible length of one. Their constructor slots are ordinary writable,
    /// configurable, non-enumerable own data properties.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/isNaN/arg-is-not-number.js")]
    [InlineData("built-ins/Number/isFinite/arg-is-not-number.js")]
    [InlineData("built-ins/Number/isInteger/arg-is-not-number.js")]
    [InlineData("built-ins/Number/isSafeInteger/arg-is-not-number.js")]
    [InlineData("built-ins/Number/isNaN/prop-desc.js")]
    [InlineData("built-ins/Number/isFinite/prop-desc.js")]
    [InlineData("built-ins/Number/isInteger/prop-desc.js")]
    [InlineData("built-ins/Number/isSafeInteger/prop-desc.js")]
    public void Number_predicates_reject_non_numbers_and_have_standard_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// Number.prototype.toExponential applies ToIntegerOrInfinity before handling special
    /// receiver values, preserves undefined as the shortest-form signal, and uses the
    /// decimal rounding and exponent spelling required by ECMA-262.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/prototype/toExponential/range.js")]
    [InlineData("built-ins/Number/prototype/toExponential/return-abrupt-tointeger-fractiondigits-symbol.js")]
    [InlineData("built-ins/Number/prototype/toExponential/return-abrupt-tointeger-fractiondigits.js")]
    [InlineData("built-ins/Number/prototype/toExponential/return-values.js")]
    [InlineData("built-ins/Number/prototype/toExponential/this-is-0-fractiondigits-is-0.js")]
    [InlineData("built-ins/Number/prototype/toExponential/this-is-0-fractiondigits-is-not-0.js")]
    [InlineData("built-ins/Number/prototype/toExponential/tointeger-fractiondigits.js")]
    [InlineData("built-ins/Number/prototype/toExponential/undefined-fractiondigits.js")]
    public void Number_toExponential_matches_spec_coercion_rounding_and_format(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// Number.prototype.toPrecision preserves undefined as the ordinary ToString path,
    /// coerces a supplied precision before handling non-finite receivers, and emits exactly
    /// the requested significant digits in fixed or exponential notation.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/prototype/toPrecision/exponential.js")]
    [InlineData("built-ins/Number/prototype/toPrecision/infinity.js")]
    [InlineData("built-ins/Number/prototype/toPrecision/nan.js")]
    [InlineData("built-ins/Number/prototype/toPrecision/precision-cannot-be-coerced-to-a-number-in-range.js")]
    [InlineData("built-ins/Number/prototype/toPrecision/range.js")]
    [InlineData("built-ins/Number/prototype/toPrecision/return-abrupt-tointeger-precision.js")]
    [InlineData("built-ins/Number/prototype/toPrecision/return-values.js")]
    [InlineData("built-ins/Number/prototype/toPrecision/this-is-0-precision-is-1.js")]
    [InlineData("built-ins/Number/prototype/toPrecision/this-is-0-precision-is-gter-than-1.js")]
    [InlineData("built-ins/Number/prototype/toPrecision/tointeger-precision.js")]
    [InlineData("built-ins/Number/prototype/toPrecision/undefined-precision-arg.js")]
    public void Number_toPrecision_matches_spec_coercion_range_and_format(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// Number.prototype.toFixed performs full ToIntegerOrInfinity coercion, rejects BigInt,
    /// validates the digit range with a guest RangeError, and only then formats NaN.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/prototype/toFixed/S15.7.4.5_A1.3_T01.js")]
    [InlineData("built-ins/Number/prototype/toFixed/S15.7.4.5_A1.3_T02.js")]
    [InlineData("built-ins/Number/prototype/toFixed/range.js")]
    [InlineData("built-ins/Number/prototype/toFixed/toFixed-tonumber-throws-typeerror-bigint.js")]
    [InlineData("built-ins/Number/prototype/toFixed/toFixed-tonumber-throws-typeerror-toprimitive.js")]
    public void Number_toFixed_matches_spec_argument_conversion_and_errors(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// Number.prototype.toString treats an absent or undefined radix as decimal, otherwise
    /// performs full ToIntegerOrInfinity coercion and reports invalid radices as RangeError.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/prototype/toString/S15.7.4.2_A1_T03.js")]
    [InlineData("built-ins/Number/prototype/toString/numeric-literal-tostring-radix-1.js")]
    [InlineData("built-ins/Number/prototype/toString/numeric-literal-tostring-radix-37.js")]
    [InlineData("built-ins/Number/prototype/toString/numeric-literal-tostring-radix-poisoned.js")]
    public void Number_toString_matches_spec_radix_conversion_and_errors(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// Number call and construct forms share ToPrimitive/ToNumber conversion, including
    /// abrupt completions and hexadecimal, binary, and octal StringNumericLiteral forms.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/S15.7.1.1_A1.js")]
    [InlineData("built-ins/Number/S9.1_A1_T1.js")]
    [InlineData("built-ins/Number/S9.3_A5_T1.js")]
    [InlineData("built-ins/Number/S9.3.1_A16.js")]
    [InlineData("built-ins/Number/S9.3.1_A17.js")]
    [InlineData("built-ins/Number/S9.3.1_A18.js")]
    [InlineData("built-ins/Number/S9.3.1_A19.js")]
    [InlineData("built-ins/Number/S9.3.1_A20.js")]
    [InlineData("built-ins/Number/S9.3.1_A21.js")]
    [InlineData("built-ins/Number/S9.3.1_A22.js")]
    [InlineData("built-ins/Number/S9.3.1_A23.js")]
    [InlineData("built-ins/Number/S9.3.1_A24.js")]
    [InlineData("built-ins/Number/S9.3.1_A25.js")]
    [InlineData("built-ins/Number/S9.3.1_A26.js")]
    [InlineData("built-ins/Number/S9.3.1_A27.js")]
    [InlineData("built-ins/Number/S9.3.1_A28.js")]
    [InlineData("built-ins/Number/S9.3.1_A29.js")]
    [InlineData("built-ins/Number/S9.3.1_A30.js")]
    [InlineData("built-ins/Number/S9.3.1_A31.js")]
    [InlineData("built-ins/Number/return-abrupt-tonumber-value-symbol.js")]
    [InlineData("built-ins/Number/return-abrupt-tonumber-value.js")]
    [InlineData("built-ins/Number/string-binary-literal.js")]
    [InlineData("built-ins/Number/string-octal-literal.js")]
    public void Number_constructor_uses_full_ToNumber_conversion(string relativePath)
        => AssertPassInBothModes(relativePath);

    /// <summary>
    /// Number exposes the standard constructor/prototype object graph, boxed-value
    /// dispatch, global parser aliases, and callable metadata in both execution modes.
    /// </summary>
    [Theory]
    [InlineData("built-ins/Number/15.7.4-1.js")]
    [InlineData("built-ins/Number/parseFloat.js")]
    [InlineData("built-ins/Number/parseFloat/not-a-constructor.js")]
    [InlineData("built-ins/Number/parseInt.js")]
    [InlineData("built-ins/Number/parseInt/not-a-constructor.js")]
    [InlineData("built-ins/Number/prototype/15.7.3.1-2.js")]
    [InlineData("built-ins/Number/prototype/S15.7.3.1_A3.js")]
    [InlineData("built-ins/Number/prototype/S15.7.4_A1.js")]
    [InlineData("built-ins/Number/prototype/S15.7.4_A2.js")]
    [InlineData("built-ins/Number/prototype/valueOf/S15.7.4.4_A1_T02.js")]
    [InlineData("built-ins/Number/S15.7.2.1_A2.js")]
    [InlineData("built-ins/Number/S15.7.2.1_A4.js")]
    [InlineData("built-ins/Number/S15.7.3_A8.js")]
    [InlineData("built-ins/Number/S15.7.5_A1_T02.js")]
    [InlineData("built-ins/Number/S15.7.5_A1_T03.js")]
    [InlineData("built-ins/Number/S15.7.5_A1_T04.js")]
    [InlineData("built-ins/Number/S15.7.5_A1_T05.js")]
    [InlineData("built-ins/Number/S15.7.5_A1_T06.js")]
    [InlineData("built-ins/Number/S15.7.5_A1_T07.js")]
    public void Number_constructor_and_prototype_have_standard_intrinsic_shape(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Error/prototype/toString/invalid-receiver.js")]
    [InlineData("built-ins/Error/prototype/toString/prop-desc.js")]
    [InlineData("built-ins/Error/prototype/toString/tostring-get-throws.js")]
    [InlineData("built-ins/Error/prototype/toString/tostring-message-throws-symbol.js")]
    [InlineData("built-ins/Error/prototype/toString/undefined-props.js")]
    public void Error_toString_uses_strict_generic_object_semantics(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/prototype/toString/Object.prototype.toString.call-date.js")]
    [InlineData("built-ins/Object/prototype/toString/Object.prototype.toString.call-error.js")]
    [InlineData("built-ins/Object/prototype/toString/Object.prototype.toString.call-regexp.js")]
    [InlineData("built-ins/Object/prototype/toString/prop-desc.js")]
    public void Object_toString_reports_standard_builtin_brands(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/prototype/isPrototypeOf/null-this-and-object-arg-throws.js")]
    [InlineData("built-ins/Object/prototype/isPrototypeOf/undefined-this-and-object-arg-throws.js")]
    [InlineData("built-ins/Object/prototype/propertyIsEnumerable/S15.2.4.7_A12.js")]
    [InlineData("built-ins/Object/prototype/propertyIsEnumerable/S15.2.4.7_A13.js")]
    [InlineData("built-ins/Object/prototype/valueOf/S15.2.4.4_A12.js")]
    [InlineData("built-ins/Object/prototype/valueOf/S15.2.4.4_A13.js")]
    [InlineData("built-ins/Object/prototype/valueOf/S15.2.4.4_A14.js")]
    [InlineData("built-ins/Object/prototype/valueOf/S15.2.4.4_A15.js")]
    public void Object_prototype_methods_reject_nullish_receivers(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_isArray_recognizes_Array_prototype()
        => AssertPassInBothModes("built-ins/Array/isArray/15.4.3.2-0-5.js");

    [Fact]
    public void Array_isArray_is_not_a_constructor()
        => AssertPassInBothModes("built-ins/Array/isArray/not-a-constructor.js");

    [Fact]
    public void Array_from_is_not_a_constructor()
        => AssertPassInBothModes("built-ins/Array/from/not-a-constructor.js");

    [Fact]
    public void Array_of_is_not_a_constructor()
        => AssertPassInBothModes("built-ins/Array/of/not-a-constructor.js");

    [Theory]
    [InlineData("built-ins/Array/prototype/pop/not-a-constructor.js")]
    [InlineData("built-ins/Array/prototype/push/not-a-constructor.js")]
    [InlineData("built-ins/Array/prototype/shift/not-a-constructor.js")]
    [InlineData("built-ins/Array/prototype/unshift/not-a-constructor.js")]
    public void Legacy_Array_mutators_are_not_constructors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_iterator_is_not_a_constructor()
        => AssertPassInBothModes("built-ins/Array/prototype/Symbol.iterator/not-a-constructor.js");

    [Theory]
    [InlineData("built-ins/Array/prototype/entries/iteration.js")]
    [InlineData("built-ins/Array/prototype/keys/iteration.js")]
    [InlineData("built-ins/Array/prototype/values/iteration.js")]
    public void Array_iterators_return_undefined_when_exhausted(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/not-a-constructor.js")]
    [InlineData("built-ins/Promise/allSettled/not-a-constructor.js")]
    [InlineData("built-ins/Promise/any/not-a-constructor.js")]
    [InlineData("built-ins/Promise/prototype/catch/not-a-constructor.js")]
    [InlineData("built-ins/Promise/prototype/finally/not-a-constructor.js")]
    [InlineData("built-ins/Promise/prototype/then/not-a-constructor.js")]
    [InlineData("built-ins/Promise/race/not-a-constructor.js")]
    [InlineData("built-ins/Promise/reject/not-a-constructor.js")]
    [InlineData("built-ins/Promise/resolve/not-a-constructor.js")]
    public void Promise_methods_are_not_constructors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void RegExp_escape_is_not_a_constructor()
        => AssertPassInBothModes("built-ins/RegExp/escape/not-a-constructor.js");

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/not-a-constructor.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/not-a-constructor.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/not-a-constructor.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.search/not-a-constructor.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/not-a-constructor.js")]
    public void RegExp_symbol_methods_are_not_constructors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/from/Array.from-descriptor.js")]
    [InlineData("built-ins/Array/isArray/descriptor.js")]
    public void Array_static_methods_have_standard_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_of_uses_the_realm_Array_constructor()
        => AssertPassInBothModes("built-ins/Array/of/of.js");

    [Theory]
    [InlineData("built-ins/Array/prototype/prop-desc.js")]
    [InlineData("built-ins/Array/prototype/proto.js")]
    public void Array_prototype_has_standard_intrinsic_shape(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_at_coerces_its_index_to_integer()
        => AssertPassInBothModes("built-ins/Array/prototype/at/index-argument-tointeger.js");

    [Fact]
    public void Array_copyWithin_treats_undefined_end_as_omitted()
        => AssertPassInBothModes("built-ins/Array/prototype/copyWithin/undefined-end.js");

    [Theory]
    [InlineData("built-ins/Array/prototype/includes/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/includes/fromIndex-equal-or-greater-length-returns-false.js")]
    [InlineData("built-ins/Array/prototype/includes/fromIndex-infinity.js")]
    [InlineData("built-ins/Array/prototype/includes/fromIndex-minus-zero.js")]
    [InlineData("built-ins/Array/prototype/includes/length-zero-returns-false.js")]
    [InlineData("built-ins/Array/prototype/includes/no-arg.js")]
    [InlineData("built-ins/Array/prototype/includes/return-abrupt-tointeger-fromindex-symbol.js")]
    [InlineData("built-ins/Array/prototype/includes/return-abrupt-tointeger-fromindex.js")]
    [InlineData("built-ins/Array/prototype/includes/samevaluezero.js")]
    [InlineData("built-ins/Array/prototype/includes/sparse.js")]
    [InlineData("built-ins/Array/prototype/includes/tointeger-fromindex.js")]
    [InlineData("built-ins/Array/prototype/includes/using-fromindex.js")]
    public void Array_includes_accepts_optional_arguments_and_generic_receivers(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/find/predicate-call-this-strict.js")]
    [InlineData("built-ins/Array/prototype/findIndex/predicate-call-this-strict.js")]
    [InlineData("built-ins/Array/prototype/findLast/predicate-call-this-strict.js")]
    [InlineData("built-ins/Array/prototype/findLastIndex/predicate-call-this-strict.js")]
    public void Array_find_callbacks_receive_undefined_this_in_strict_mode(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/join/S15.4.4.5_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/join/S15.4.4.5_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/join/S15.4.4.5_A2_T3.js")]
    [InlineData("built-ins/Array/prototype/join/S15.4.4.5_A2_T4.js")]
    [InlineData("built-ins/Array/prototype/join/S15.4.4.5_A4_T3.js")]
    public void Array_join_is_generic_for_array_like_objects(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/entries/returns-iterator.js")]
    [InlineData("built-ins/Array/prototype/entries/returns-iterator-from-object.js")]
    [InlineData("built-ins/Array/prototype/keys/returns-iterator.js")]
    [InlineData("built-ins/Array/prototype/keys/returns-iterator-from-object.js")]
    [InlineData("built-ins/Array/prototype/values/returns-iterator.js")]
    [InlineData("built-ins/Array/prototype/values/returns-iterator-from-object.js")]
    public void Array_iterator_methods_share_the_Array_iterator_prototype(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Boolean_constructor_reports_its_spec_length()
        => AssertPassInBothModes("built-ins/Boolean/S15.6.3_A3.js");

    [Fact]
    public void Boolean_constructor_owns_its_prototype_property()
        => AssertPassInBothModes("built-ins/Boolean/S15.6.3_A1.js");

    [Fact]
    public void Boxed_Boolean_instances_inherit_from_Boolean_prototype()
        => AssertPassInBothModes("built-ins/Boolean/S15.6.2.1_A2.js");

    [Fact]
    public void Boolean_prototype_inherits_from_Object_prototype()
        => AssertPassInBothModes("built-ins/Boolean/prototype/S15.6.4_A2.js");

    [Fact]
    public void Boolean_call_coerces_nullish_values_to_false()
        => AssertPassInBothModes("built-ins/Boolean/S15.6.1.1_A1_T4.js");

    [Fact]
    public void Deleted_Boolean_toString_falls_back_to_Object_prototype()
        => AssertPassInBothModes("built-ins/Boolean/S15.6.2.1_A4.js");

    [Fact]
    public void Boolean_prototype_coerces_to_false_for_loose_equality()
        => AssertPassInBothModes(
            "built-ins/Boolean/prototype/S15.6.3.1_A1.js");

    [Fact]
    public void Error_prototype_exposes_its_standard_name()
        => AssertPassInBothModes("built-ins/Error/name.js");

    [Fact]
    public void Error_prototype_exposes_its_standard_message_descriptor()
        => AssertPassInBothModes("built-ins/Error/prototype/message/prop-desc.js");

    [Fact]
    public void Error_constructor_owns_its_prototype_property()
        => AssertPassInBothModes("built-ins/Error/prototype/S15.11.3.1_A4_T1.js");

    [Fact]
    public void Error_prototype_inherits_from_Object_prototype()
        => AssertPassInBothModes("built-ins/Error/prototype/S15.11.4_A1.js");

    [Theory]
    [InlineData("built-ins/Math/PI/prop-desc.js")]
    [InlineData("built-ins/Math/abs/prop-desc.js")]
    [InlineData("built-ins/Math/E/prop-desc.js")]
    [InlineData("built-ins/Math/LN10/prop-desc.js")]
    [InlineData("built-ins/Math/LN2/prop-desc.js")]
    [InlineData("built-ins/Math/LOG10E/prop-desc.js")]
    [InlineData("built-ins/Math/LOG2E/prop-desc.js")]
    [InlineData("built-ins/Math/SQRT1_2/prop-desc.js")]
    [InlineData("built-ins/Math/SQRT2/prop-desc.js")]
    [InlineData("built-ins/Math/acos/prop-desc.js")]
    [InlineData("built-ins/Math/acosh/prop-desc.js")]
    [InlineData("built-ins/Math/asin/prop-desc.js")]
    [InlineData("built-ins/Math/asinh/prop-desc.js")]
    [InlineData("built-ins/Math/atan/prop-desc.js")]
    [InlineData("built-ins/Math/atan2/prop-desc.js")]
    [InlineData("built-ins/Math/atanh/prop-desc.js")]
    [InlineData("built-ins/Math/cbrt/prop-desc.js")]
    [InlineData("built-ins/Math/ceil/prop-desc.js")]
    [InlineData("built-ins/Math/clz32/prop-desc.js")]
    [InlineData("built-ins/Math/cos/prop-desc.js")]
    [InlineData("built-ins/Math/cosh/prop-desc.js")]
    [InlineData("built-ins/Math/exp/prop-desc.js")]
    [InlineData("built-ins/Math/expm1/prop-desc.js")]
    [InlineData("built-ins/Math/floor/prop-desc.js")]
    [InlineData("built-ins/Math/fround/prop-desc.js")]
    [InlineData("built-ins/Math/hypot/prop-desc.js")]
    [InlineData("built-ins/Math/imul/prop-desc.js")]
    [InlineData("built-ins/Math/log/prop-desc.js")]
    [InlineData("built-ins/Math/log10/prop-desc.js")]
    [InlineData("built-ins/Math/log1p/prop-desc.js")]
    [InlineData("built-ins/Math/log2/prop-desc.js")]
    [InlineData("built-ins/Math/max/prop-desc.js")]
    [InlineData("built-ins/Math/min/prop-desc.js")]
    [InlineData("built-ins/Math/pow/prop-desc.js")]
    [InlineData("built-ins/Math/random/prop-desc.js")]
    [InlineData("built-ins/Math/round/prop-desc.js")]
    [InlineData("built-ins/Math/sign/prop-desc.js")]
    [InlineData("built-ins/Math/sin/prop-desc.js")]
    [InlineData("built-ins/Math/sinh/prop-desc.js")]
    [InlineData("built-ins/Math/sqrt/prop-desc.js")]
    [InlineData("built-ins/Math/tan/prop-desc.js")]
    [InlineData("built-ins/Math/tanh/prop-desc.js")]
    [InlineData("built-ins/Math/trunc/prop-desc.js")]
    public void Math_members_have_standard_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Math_inherits_from_Object_prototype()
        => AssertPassInBothModes("built-ins/Math/proto.js");

    [Fact]
    public void Math_sign_preserves_special_values()
        => AssertPassInBothModes("built-ins/Math/sign/sign-specialVals.js");

    [Fact]
    public void Math_round_preserves_negative_zero()
        => AssertPassInBothModes("built-ins/Math/round/S15.8.2.15_A3.js");

    [Fact]
    public void Math_max_prefers_positive_zero()
        => AssertPassInBothModes("built-ins/Math/max/zeros.js");

    [Fact]
    public void Math_min_prefers_negative_zero()
        => AssertPassInBothModes("built-ins/Math/min/zeros.js");

    [Fact]
    public void Math_max_coerces_every_argument()
        => AssertPassInBothModes("built-ins/Math/max/Math.max_each-element-coerced.js");

    [Fact]
    public void Math_min_coerces_every_argument()
        => AssertPassInBothModes("built-ins/Math/min/Math.min_each-element-coerced.js");

    [Fact]
    public void Math_hypot_coerces_before_inspection()
        => AssertPassInBothModes("built-ins/Math/hypot/Math.hypot_ToNumberErr.js");

    [Theory]
    [InlineData("built-ins/Math/pow/applying-the-exp-operator_A1.js")]
    [InlineData("built-ins/Math/pow/applying-the-exp-operator_A7.js")]
    [InlineData("built-ins/Math/pow/applying-the-exp-operator_A8.js")]
    public void Math_pow_handles_nan_and_infinite_exponents(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/is/same-value-x-y-empty.js")]
    [InlineData("built-ins/Object/is/same-value-x-y-undefined.js")]
    [InlineData("built-ins/Object/is/not-same-value-x-y-null.js")]
    [InlineData("built-ins/Object/is/not-same-value-x-y-number.js")]
    public void Object_is_treats_omitted_arguments_as_undefined(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/hasOwn/toobject_before_topropertykey.js")]
    [InlineData("built-ins/Object/hasOwn/toobject_null.js")]
    [InlineData("built-ins/Object/hasOwn/toobject_undefined.js")]
    public void Object_hasOwn_rejects_nullish_targets_before_coercing_keys(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/keys/15.2.3.14-1-1.js")]
    [InlineData("built-ins/Object/keys/15.2.3.14-1-2.js")]
    [InlineData("built-ins/Object/keys/15.2.3.14-1-3.js")]
    [InlineData("built-ins/Object/keys/15.2.3.14-1-4.js")]
    [InlineData("built-ins/Object/keys/15.2.3.14-1-5.js")]
    [InlineData("built-ins/Object/entries/exception-not-object-coercible.js")]
    [InlineData("built-ins/Object/entries/primitive-booleans.js")]
    [InlineData("built-ins/Object/entries/primitive-numbers.js")]
    [InlineData("built-ins/Object/entries/primitive-strings.js")]
    [InlineData("built-ins/Object/entries/primitive-symbols.js")]
    [InlineData("built-ins/Object/values/exception-not-object-coercible.js")]
    [InlineData("built-ins/Object/values/primitive-booleans.js")]
    [InlineData("built-ins/Object/values/primitive-numbers.js")]
    [InlineData("built-ins/Object/values/primitive-strings.js")]
    [InlineData("built-ins/Object/values/primitive-symbols.js")]
    public void Object_enumeration_methods_apply_ToObject(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/entries/inherited-properties-omitted.js")]
    [InlineData("built-ins/Object/values/inherited-properties-omitted.js")]
    public void Object_enumeration_methods_omit_inherited_properties(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Object_keys_returns_integer_indices_before_creation_ordered_strings()
        => AssertPass(
            "built-ins/Object/keys/return-order.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Object_keys_observes_proxy_traps_in_spec_order()
        => AssertPass(
            "built-ins/Object/keys/property-traps-order-with-proxied-array.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Object_entries_returns_integer_indices_before_creation_ordered_strings()
        => AssertPass(
            "built-ins/Object/entries/return-order.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Object_values_uses_the_snapshotted_spec_key_order()
        => AssertPass(
            "built-ins/Object/values/return-order.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Object/entries/order-after-define-property-with-function.js")]
    [InlineData("built-ins/Object/entries/order-after-define-property.js")]
    [InlineData("built-ins/Object/keys/order-after-define-property-with-function.js")]
    [InlineData("built-ins/Object/keys/order-after-define-property.js")]
    [InlineData("built-ins/Object/values/order-after-define-property.js")]
    public void Object_enumeration_preserves_key_creation_order(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_defineProperty_passes_the_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/defineProperty/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Proxy/defineProperty/trap-is-null-target-is-proxy.js")]
    [InlineData("built-ins/Proxy/defineProperty/trap-is-undefined-target-is-proxy.js")]
    [InlineData("built-ins/Proxy/defineProperty/trap-is-undefined.js")]
    public void Proxy_defineProperty_forwards_missing_traps(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_defineProperty_boolean_coerces_false_trap_results()
        => AssertPass(
            "built-ins/Proxy/defineProperty/trap-return-is-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_defineProperty_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/defineProperty/return-is-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_defineProperty_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/defineProperty/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_defineProperty_rejects_additions_to_nonextensible_targets()
        => AssertPass(
            "built-ins/Proxy/defineProperty/targetdesc-undefined-target-is-not-extensible.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_defineProperty_rejects_phantom_nonconfigurable_properties()
        => AssertPass(
            "built-ins/Proxy/defineProperty/targetdesc-undefined-not-configurable-descriptor.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_defineProperty_cannot_hide_configurable_target_properties()
        => AssertPass(
            "built-ins/Proxy/defineProperty/targetdesc-configurable-desc-not-configurable.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Proxy/defineProperty/targetdesc-not-compatible-descriptor.js")]
    [InlineData("built-ins/Proxy/defineProperty/targetdesc-not-compatible-descriptor-not-configurable-target.js")]
    [InlineData("built-ins/Proxy/defineProperty/targetdesc-not-configurable-writable-desc-not-writable.js")]
    public void Proxy_defineProperty_enforces_target_descriptor_invariants(
        string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_defineProperty_allows_compatible_target_updates()
        => AssertPass(
            "built-ins/Proxy/defineProperty/return-boolean-and-define-target.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_defineProperty_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/defineProperty/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_ownKeys_passes_the_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/ownKeys/call-parameters-object-keys.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Proxy/ownKeys/call-parameters-object-getownpropertynames.js")]
    [InlineData("built-ins/Proxy/ownKeys/call-parameters-object-getownpropertysymbols.js")]
    public void Proxy_ownKeys_drives_own_property_introspection(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Proxy/ownKeys/trap-is-missing-target-is-proxy.js")]
    [InlineData("built-ins/Proxy/ownKeys/trap-is-null-target-is-proxy.js")]
    [InlineData("built-ins/Proxy/ownKeys/trap-is-undefined-target-is-proxy.js")]
    [InlineData("built-ins/Proxy/ownKeys/trap-is-undefined.js")]
    public void Proxy_ownKeys_forwards_missing_traps(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_ownKeys_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/ownKeys/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_ownKeys_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/ownKeys/return-is-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_ownKeys_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/ownKeys/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_ownKeys_rejects_non_object_results()
        => AssertPass(
            "built-ins/Proxy/ownKeys/return-not-list-object-throws.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Proxy/ownKeys/return-type-throws-array.js")]
    [InlineData("built-ins/Proxy/ownKeys/return-type-throws-boolean.js")]
    [InlineData("built-ins/Proxy/ownKeys/return-type-throws-null.js")]
    [InlineData("built-ins/Proxy/ownKeys/return-type-throws-number.js")]
    [InlineData("built-ins/Proxy/ownKeys/return-type-throws-object.js")]
    [InlineData("built-ins/Proxy/ownKeys/return-type-throws-undefined.js")]
    public void Proxy_ownKeys_rejects_non_property_key_entries(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Proxy/ownKeys/return-duplicate-entries-throws.js")]
    [InlineData("built-ins/Proxy/ownKeys/return-duplicate-symbol-entries-throws.js")]
    public void Proxy_ownKeys_rejects_duplicate_property_keys(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_ownKeys_requires_nonconfigurable_target_keys()
        => AssertPass(
            "built-ins/Proxy/ownKeys/extensible-return-trap-result-absent-not-configurable-keys.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_ownKeys_allows_all_nonconfigurable_target_keys()
        => AssertPass(
            "built-ins/Proxy/ownKeys/return-all-non-configurable-keys.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_ownKeys_allows_extensible_target_variations()
        => AssertPass(
            "built-ins/Proxy/ownKeys/extensible-return-trap-result.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Proxy/ownKeys/not-extensible-missing-keys-throws.js")]
    [InlineData("built-ins/Proxy/ownKeys/not-extensible-new-keys-throws.js")]
    [InlineData("built-ins/Proxy/ownKeys/not-extensible-return-keys.js")]
    public void Proxy_ownKeys_matches_nonextensible_target_keys(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_accepts_undefined_trap_results()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/result-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_accepts_missing_target_properties()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/result-is-undefined-targetdesc-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_forwards_missing_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/trap-is-missing-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_forwards_null_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/trap-is-null-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_forwards_undefined_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/trap-is-undefined-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_forwards_undefined_traps()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/trap-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/return-is-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_rejects_primitive_trap_results()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/result-type-is-not-object-nor-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_rejects_invalid_descriptors()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/resultdesc-is-invalid-descriptor.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_rejects_hidden_properties_on_fixed_targets()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/result-is-undefined-target-is-not-extensible.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_rejects_hidden_nonconfigurable_properties()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/result-is-undefined-targetdesc-is-not-configurable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_rejects_falsely_frozen_writable_properties()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/resultdesc-is-not-configurable-not-writable-targetdesc-is-writable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_rejects_falsely_nonconfigurable_properties()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/resultdesc-is-not-configurable-targetdesc-is-configurable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_rejects_phantom_nonconfigurable_properties()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/resultdesc-is-not-configurable-targetdesc-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_returns_complete_configurable_descriptors()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/resultdesc-return-configurable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getOwnPropertyDescriptor_returns_complete_fixed_descriptors()
        => AssertPass(
            "built-ins/Proxy/getOwnPropertyDescriptor/resultdesc-return-not-configurable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_accepts_custom_prototypes_for_extensible_targets()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/extensible-target-return-handlerproto.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_supports_instanceof_custom_prototypes()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/instanceof-custom-return-accepted.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_rejects_instanceof_lies_for_fixed_targets()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/instanceof-target-not-extensible-not-same-proto-throws.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_rejects_mismatched_fixed_target_prototypes()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/not-extensible-not-same-proto-throws.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_accepts_matching_fixed_target_prototypes()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/not-extensible-same-proto.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/return-is-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_forwards_missing_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/trap-is-missing-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_forwards_null_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/trap-is-null-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_forwards_undefined_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/trap-is-undefined-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_forwards_undefined_traps()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/trap-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_rejects_boolean_results()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/trap-result-neither-object-nor-null-throws-boolean.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_rejects_numeric_results()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/trap-result-neither-object-nor-null-throws-number.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_rejects_string_results()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/trap-result-neither-object-nor-null-throws-string.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_rejects_symbol_results()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/trap-result-neither-object-nor-null-throws-symbol.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_getPrototypeOf_rejects_undefined_results()
        => AssertPass(
            "built-ins/Proxy/getPrototypeOf/trap-result-neither-object-nor-null-throws-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/isExtensible/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/isExtensible/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/isExtensible/return-is-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_boolean_coerces_trap_results()
        => AssertPass(
            "built-ins/Proxy/isExtensible/return-is-boolean.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_rejects_results_different_from_target()
        => AssertPass(
            "built-ins/Proxy/isExtensible/return-is-different-from-target.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_accepts_results_matching_target()
        => AssertPass(
            "built-ins/Proxy/isExtensible/return-same-result-from-target.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_forwards_missing_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/isExtensible/trap-is-missing-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/isExtensible/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_forwards_null_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/isExtensible/trap-is-null-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_forwards_undefined_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/isExtensible/trap-is-undefined-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_isExtensible_forwards_undefined_traps()
        => AssertPass(
            "built-ins/Proxy/isExtensible/trap-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_preventExtensions_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/preventExtensions/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_preventExtensions_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/preventExtensions/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_preventExtensions_rejects_false_trap_results()
        => AssertPass(
            "built-ins/Proxy/preventExtensions/return-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_preventExtensions_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/preventExtensions/return-is-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_preventExtensions_rejects_extensible_target_lies()
        => AssertPass(
            "built-ins/Proxy/preventExtensions/return-true-target-is-extensible.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_preventExtensions_accepts_nonextensible_targets()
        => AssertPass(
            "built-ins/Proxy/preventExtensions/return-true-target-is-not-extensible.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_preventExtensions_forwards_missing_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/preventExtensions/trap-is-missing-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_preventExtensions_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/preventExtensions/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_preventExtensions_forwards_null_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/preventExtensions/trap-is-null-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_preventExtensions_forwards_undefined_traps()
        => AssertPass(
            "built-ins/Proxy/preventExtensions/trap-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_observes_internal_call_order()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/internals-call-order.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_rejects_fixed_target_prototype_lies()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/not-extensible-target-not-same-target-prototype.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_accepts_matching_fixed_target_prototypes()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/not-extensible-target-same-target-prototype.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_propagates_trap_lookup_errors()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/return-abrupt-from-get-trap.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_propagates_target_extensibility_errors()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/return-abrupt-from-isextensible-target.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_propagates_target_prototype_errors()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/return-abrupt-from-target-getprototypeof.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/return-abrupt-from-trap.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_coerces_false_trap_results()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/toboolean-trap-result-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_coerces_truthy_results_for_extensible_targets()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/toboolean-trap-result-true-target-is-extensible.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_forwards_missing_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/trap-is-missing-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_forwards_null_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/trap-is-null-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_forwards_undefined_and_null_traps()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/trap-is-undefined-or-null.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_setPrototypeOf_forwards_undefined_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/setPrototypeOf/trap-is-undefined-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_rejects_nonconfigurable_target_properties()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/targetdesc-is-not-configurable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_preserves_false_trap_results()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/boolean-trap-result-boolean-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_preserves_true_trap_results()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/boolean-trap-result-boolean-true.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_allows_false_results_in_sloppy_code()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/return-false-not-strict.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_throws_for_false_results_in_strict_code()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/return-false-strict.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/return-is-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_rejects_hidden_fixed_target_properties()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/targetdesc-is-configurable-target-is-not-extensible.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_accepts_absent_target_properties()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/targetdesc-is-undefined-return-true.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_forwards_missing_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/trap-is-missing-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_forwards_null_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/trap-is-null-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_forwards_undefined_traps_in_sloppy_code()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/trap-is-undefined-not-strict.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_forwards_undefined_traps_in_strict_code()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/trap-is-undefined-strict.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_deleteProperty_forwards_undefined_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/deleteProperty/trap-is-undefined-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/has/call-in.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_rejects_hidden_nonconfigurable_properties()
        => AssertPass(
            "built-ins/Proxy/has/return-false-targetdesc-not-configurable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_handles_indexed_prototype_queries()
        => AssertPass(
            "built-ins/Proxy/has/call-in-prototype-index.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_handles_named_prototype_queries()
        => AssertPass(
            "built-ins/Proxy/has/call-in-prototype.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_handles_object_create_prototypes()
        => AssertPass(
            "built-ins/Proxy/has/call-object-create.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/has/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_rejects_hidden_properties_on_fixed_targets()
        => AssertPass(
            "built-ins/Proxy/has/return-false-target-not-extensible.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_allows_hidden_configurable_properties()
        => AssertPass(
            "built-ins/Proxy/has/return-false-target-prop-exists.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_propagates_abrupt_in_traps()
        => AssertPass(
            "built-ins/Proxy/has/return-is-abrupt-in.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_accepts_true_results_for_existing_properties()
        => AssertPass(
            "built-ins/Proxy/has/return-true-target-prop-exists.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_accepts_true_results_for_phantom_properties()
        => AssertPass(
            "built-ins/Proxy/has/return-true-without-same-target-prop.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_forwards_missing_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/has/trap-is-missing-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/has/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_forwards_null_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/has/trap-is-null-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_forwards_undefined_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/has/trap-is-undefined-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_has_forwards_undefined_traps()
        => AssertPass(
            "built-ins/Proxy/has/trap-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/get/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_rejects_fixed_data_property_lies()
        => AssertPass(
            "built-ins/Proxy/get/not-same-value-configurable-false-writable-false-throws.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_rejects_fixed_accessor_lies()
        => AssertPass(
            "built-ins/Proxy/get/accessor-get-is-undefined-throws.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/get/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/get/return-is-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_accepts_accessor_trap_results()
        => AssertPass(
            "built-ins/Proxy/get/return-trap-result-accessor-property.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_accepts_writable_fixed_data_results()
        => AssertPass(
            "built-ins/Proxy/get/return-trap-result-configurable-false-writable-true.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_accepts_configurable_accessor_results()
        => AssertPass(
            "built-ins/Proxy/get/return-trap-result-configurable-true-assessor-get-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_accepts_configurable_readonly_data_results()
        => AssertPass(
            "built-ins/Proxy/get/return-trap-result-configurable-true-writable-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_accepts_same_value_for_fixed_data_properties()
        => AssertPass(
            "built-ins/Proxy/get/return-trap-result-same-value-configurable-false-writable-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_returns_trap_results()
        => AssertPass(
            "built-ins/Proxy/get/return-trap-result.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_forwards_missing_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/get/trap-is-missing-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/get/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_forwards_null_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/get/trap-is-null-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_returns_undefined_for_absent_forwarded_properties()
        => AssertPass(
            "built-ins/Proxy/get/trap-is-undefined-no-property.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_forwards_explicit_receivers()
        => AssertPass(
            "built-ins/Proxy/get/trap-is-undefined-receiver.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_forwards_undefined_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/get/trap-is-undefined-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_get_forwards_undefined_traps()
        => AssertPass(
            "built-ins/Proxy/get/trap-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/set/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_rejects_changes_to_fixed_data_properties()
        => AssertPass(
            "built-ins/Proxy/set/target-property-is-not-configurable-not-writable-not-equal-to-v.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_preserves_false_boolean_results()
        => AssertPass(
            "built-ins/Proxy/set/boolean-trap-result-is-false-boolean-return-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_coerces_null_trap_results_to_false()
        => AssertPass(
            "built-ins/Proxy/set/boolean-trap-result-is-false-null-return-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_coerces_zero_trap_results_to_false()
        => AssertPass(
            "built-ins/Proxy/set/boolean-trap-result-is-false-number-return-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_coerces_empty_string_trap_results_to_false()
        => AssertPass(
            "built-ins/Proxy/set/boolean-trap-result-is-false-string-return-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_coerces_undefined_trap_results_to_false()
        => AssertPass(
            "built-ins/Proxy/set/boolean-trap-result-is-false-undefined-return-false.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_dispatches_dunder_proto_prototype_writes()
        => AssertPass(
            "built-ins/Proxy/set/call-parameters-prototype-dunder-proto.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_dispatches_indexed_prototype_writes()
        => AssertPass(
            "built-ins/Proxy/set/call-parameters-prototype-index.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_dispatches_named_prototype_writes()
        => AssertPass(
            "built-ins/Proxy/set/call-parameters-prototype.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/set/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/set/return-is-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_accepts_configurable_accessor_lies()
        => AssertPass(
            "built-ins/Proxy/set/return-true-target-property-accessor-is-configurable-set-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_accepts_fixed_accessors_with_setters()
        => AssertPass(
            "built-ins/Proxy/set/return-true-target-property-accessor-is-not-configurable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_accepts_fixed_writable_data_properties()
        => AssertPass(
            "built-ins/Proxy/set/return-true-target-property-is-not-configurable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_accepts_same_value_for_fixed_data_properties()
        => AssertPass(
            "built-ins/Proxy/set/return-true-target-property-is-not-writable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_rejects_fixed_accessors_without_setters()
        => AssertPass(
            "built-ins/Proxy/set/target-property-is-accessor-not-configurable-set-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_forwards_repeated_indexed_writes()
        => AssertPass(
            "built-ins/Proxy/set/trap-is-missing-receiver-multiple-calls-index.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_forwards_repeated_named_writes()
        => AssertPass(
            "built-ins/Proxy/set/trap-is-missing-receiver-multiple-calls.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_forwards_missing_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/set/trap-is-missing-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/set/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_respects_explicit_null_receivers()
        => AssertPass(
            "built-ins/Proxy/set/trap-is-null-receiver.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_forwards_null_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/set/trap-is-null-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_forwards_undefined_traps_for_new_properties()
        => AssertPass(
            "built-ins/Proxy/set/trap-is-undefined-no-property.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_forwards_undefined_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/set/trap-is-undefined-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_set_forwards_undefined_traps()
        => AssertPass(
            "built-ins/Proxy/set/trap-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_returns_trap_results()
        => AssertPass(
            "built-ins/Proxy/apply/call-result.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/apply/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/apply/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/apply/return-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_forwards_missing_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/apply/trap-is-missing-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/apply/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_forwards_null_traps()
        => AssertPass(
            "built-ins/Proxy/apply/trap-is-null.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_forwards_null_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/apply/trap-is-null-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_forwards_absent_apply_properties()
        => AssertPass(
            "built-ins/Proxy/apply/trap-is-undefined-no-property.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_forwards_undefined_traps_to_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/apply/trap-is-undefined-target-is-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_apply_forwards_undefined_traps()
        => AssertPass(
            "built-ins/Proxy/apply/trap-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_returns_object_trap_results()
        => AssertPass(
            "built-ins/Proxy/construct/call-result.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_passes_spec_trap_arguments()
        => AssertPass(
            "built-ins/Proxy/construct/call-parameters.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_passes_new_target_to_traps()
        => AssertPass(
            "built-ins/Proxy/construct/call-parameters-new-target.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_rejects_revoked_proxies()
        => AssertPass(
            "built-ins/Proxy/construct/null-handler.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_propagates_abrupt_trap_completion()
        => AssertPass(
            "built-ins/Proxy/construct/return-is-abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_rejects_boolean_trap_results()
        => AssertPass(
            "built-ins/Proxy/construct/return-not-object-throws-boolean.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_rejects_null_trap_results()
        => AssertPass(
            "built-ins/Proxy/construct/return-not-object-throws-null.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_rejects_number_trap_results()
        => AssertPass(
            "built-ins/Proxy/construct/return-not-object-throws-number.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_rejects_string_trap_results()
        => AssertPass(
            "built-ins/Proxy/construct/return-not-object-throws-string.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_rejects_symbol_trap_results()
        => AssertPass(
            "built-ins/Proxy/construct/return-not-object-throws-symbol.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_rejects_undefined_trap_results()
        => AssertPass(
            "built-ins/Proxy/construct/return-not-object-throws-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_rejects_noncallable_traps()
        => AssertPass(
            "built-ins/Proxy/construct/trap-is-not-callable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_forwards_null_traps()
        => AssertPass(
            "built-ins/Proxy/construct/trap-is-null.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_forwards_absent_construct_properties()
        => AssertPass(
            "built-ins/Proxy/construct/trap-is-undefined-no-property.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_construct_forwards_undefined_traps()
        => AssertPass(
            "built-ins/Proxy/construct/trap-is-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocable_exposes_the_builtin_function()
        => AssertPass(
            "built-ins/Proxy/revocable/builtin.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocable_rejects_revoked_handlers()
        => AssertPass(
            "built-ins/Proxy/revocable/handler-is-revoked-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocable_has_standard_length()
        => AssertPass(
            "built-ins/Proxy/revocable/length.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocable_has_standard_name()
        => AssertPass(
            "built-ins/Proxy/revocable/name.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocable_is_not_a_constructor()
        => AssertPass(
            "built-ins/Proxy/revocable/not-a-constructor.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocable_returns_a_proxy()
        => AssertPass(
            "built-ins/Proxy/revocable/proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocation_function_is_extensible()
        => AssertPass(
            "built-ins/Proxy/revocable/revocation-function-extensible.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocation_function_has_zero_length()
        => AssertPass(
            "built-ins/Proxy/revocable/revocation-function-length.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocation_function_has_empty_name()
        => AssertPass(
            "built-ins/Proxy/revocable/revocation-function-name.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocation_function_is_not_a_constructor()
        => AssertPass(
            "built-ins/Proxy/revocable/revocation-function-not-a-constructor.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocation_function_has_no_prototype()
        => AssertPass(
            "built-ins/Proxy/revocable/revocation-function-prototype.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocation_function_has_standard_property_order()
        => AssertPass(
            "built-ins/Proxy/revocable/revocation-function-property-order.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocation_returns_undefined()
        => AssertPass(
            "built-ins/Proxy/revocable/revoke-returns-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_repeated_revocation_returns_undefined()
        => AssertPass(
            "built-ins/Proxy/revocable/revoke-consecutive-call-returns-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocation_disables_proxy_operations()
        => AssertPass(
            "built-ins/Proxy/revocable/revoke.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocable_accepts_revoked_proxy_targets()
        => AssertPass(
            "built-ins/Proxy/revocable/target-is-revoked-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_revocable_preserves_revoked_callable_targets()
        => AssertPass(
            "built-ins/Proxy/revocable/target-is-revoked-function-proxy.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_creates_proxy_objects()
        => AssertPass(
            "built-ins/Proxy/constructor.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_boolean_handlers()
        => AssertPass(
            "built-ins/Proxy/create-handler-not-object-throw-boolean.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_null_handlers()
        => AssertPass(
            "built-ins/Proxy/create-handler-not-object-throw-null.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_number_handlers()
        => AssertPass(
            "built-ins/Proxy/create-handler-not-object-throw-number.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_string_handlers()
        => AssertPass(
            "built-ins/Proxy/create-handler-not-object-throw-string.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_symbol_handlers()
        => AssertPass(
            "built-ins/Proxy/create-handler-not-object-throw-symbol.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_undefined_handlers()
        => AssertPass(
            "built-ins/Proxy/create-handler-not-object-throw-undefined.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_boolean_targets()
        => AssertPass(
            "built-ins/Proxy/create-target-not-object-throw-boolean.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_null_targets()
        => AssertPass(
            "built-ins/Proxy/create-target-not-object-throw-null.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_number_targets()
        => AssertPass(
            "built-ins/Proxy/create-target-not-object-throw-number.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_string_targets()
        => AssertPass(
            "built-ins/Proxy/create-target-not-object-throw-string.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Proxy_constructor_rejects_symbol_targets()
        => AssertPass(
            "built-ins/Proxy/create-target-not-object-throw-symbol.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-623.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-624.js")]
    public void Date_prototype_methods_expose_standard_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/prototype/__lookupGetter__/lookup-own-acsr-w-getter.js")]
    [InlineData("built-ins/Object/prototype/__lookupGetter__/lookup-proto-acsr-w-getter.js")]
    [InlineData("built-ins/Object/prototype/__lookupSetter__/lookup-own-acsr-w-setter.js")]
    [InlineData("built-ins/Object/prototype/__lookupSetter__/lookup-proto-acsr-w-setter.js")]
    public void Object_legacy_accessor_lookup_walks_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/S15.2.1.1_A2_T11.js")]
    [InlineData("built-ins/Object/S15.2.1.1_A3_T2.js")]
    [InlineData("built-ins/Object/S15.2.2.1_A1_T1.js")]
    [InlineData("built-ins/Object/S15.2.2.1_A1_T2.js")]
    [InlineData("built-ins/Object/S15.2.2.1_A1_T3.js")]
    [InlineData("built-ins/Object/S15.2.2.1_A1_T4.js")]
    [InlineData("built-ins/Object/S15.2.2.1_A1_T5.js")]
    [InlineData("built-ins/Object/S15.2.2.1_A2_T7.js")]
    [InlineData("built-ins/Object/S15.2.2.1_A6_T2.js")]
    public void Object_call_and_construction_apply_legacy_coercion(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptors/inherited-properties-omitted.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptors/proxy-undefined-descriptor.js")]
    public void Object_getOwnPropertyDescriptors_uses_own_descriptor_semantics(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Object_getOwnPropertyDescriptors_preserves_source_key_order()
        => AssertPass(
            "built-ins/Object/getOwnPropertyDescriptors/order-after-define-property.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Object_getOwnPropertyDescriptors_preserves_proxy_key_order()
        => AssertPass(
            "built-ins/Object/getOwnPropertyDescriptors/proxy-no-ownkeys-returned-keys-order.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Object_fromEntries_rejects_an_omitted_iterable()
        => AssertPassInBothModes("built-ins/Object/fromEntries/requires-argument.js");

    [Fact]
    public void Object_getOwnPropertyDescriptors_rejects_nullish_targets()
        => AssertPassInBothModes(
            "built-ins/Object/getOwnPropertyDescriptors/exception-not-object-coercible.js");

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyNames/15.2.3.4-1-2.js")]
    [InlineData("built-ins/Object/getOwnPropertyNames/non-object-argument-invalid.js")]
    public void Object_getOwnPropertyNames_rejects_nullish_targets(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyNames/15.2.3.4-4-39.js")]
    [InlineData("built-ins/Object/getOwnPropertyNames/15.2.3.4-4-43.js")]
    [InlineData("built-ins/Object/getOwnPropertyNames/15.2.3.4-4-47.js")]
    [InlineData("built-ins/Object/getOwnPropertyNames/15.2.3.4-4-48.js")]
    public void Object_getOwnPropertyNames_includes_own_expandos(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Object_getOwnPropertyNames_preserves_creation_order_on_redefinition()
        => AssertPass(
            "built-ins/Object/getOwnPropertyNames/order-after-define-property.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Object_getOwnPropertySymbols_rejects_nullish_targets()
        => AssertPassInBothModes(
            "built-ins/Object/getOwnPropertySymbols/non-object-argument-invalid.js");

    [Fact]
    public void Object_getOwnPropertySymbols_preserves_creation_order()
        => AssertPassInBothModes(
            "built-ins/Object/getOwnPropertySymbols/order-after-define-property.js");

    [Fact]
    public void Reflect_ownKeys_preserves_creation_order_after_redefinition()
        => AssertPass(
            "built-ins/Reflect/ownKeys/order-after-define-property.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Reflect/ownKeys/return-on-corresponding-order-large-index.js")]
    [InlineData("built-ins/Reflect/ownKeys/return-on-corresponding-order.js")]
    [InlineData("built-ins/Reflect/ownKeys/return-array-with-own-keys-only.js")]
    [InlineData("built-ins/Reflect/ownKeys/return-empty-array.js")]
    public void Reflect_ownKeys_returns_spec_ordered_property_keys(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Reflect_ownKeys_includes_non_enumerable_array_and_object_keys()
        => AssertPass(
            "built-ins/Reflect/ownKeys/return-non-enumerable-keys.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Reflect/ownKeys/target-is-not-object-throws.js")]
    [InlineData("built-ins/Reflect/ownKeys/target-is-symbol-throws.js")]
    public void Reflect_ownKeys_rejects_primitive_targets(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Reflect_ownKeys_propagates_abrupt_proxy_traps()
        => AssertPass(
            "built-ins/Reflect/ownKeys/return-abrupt-from-result.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-0-3.js")]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-1-3.js")]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-1-4.js")]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-1.js")]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-2-18.js")]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-2-22.js")]
    public void Object_getPrototypeOf_applies_ToObject(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/preventExtensions/15.2.3.10-3-3.js")]
    [InlineData("built-ins/Object/preventExtensions/15.2.3.10-3-13.js")]
    public void Object_preventExtensions_handles_function_objects(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/setPrototypeOf/o-not-obj-coercible.js")]
    [InlineData("built-ins/Object/setPrototypeOf/success.js")]
    public void Object_setPrototypeOf_links_without_copying_properties(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Object_create_reports_its_spec_length()
        => AssertPassInBothModes("built-ins/Object/create/15.2.3.5-0-2.js");

    [Fact]
    public void Object_create_reads_descriptors_from_Error_objects()
        => AssertPassInBothModes("built-ins/Object/create/15.2.3.5-4-14.js");

    [Fact]
    public void Object_getOwnPropertyDescriptor_rejects_undefined_target()
        => AssertPassInBothModes(
            "built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-1-1.js");

    [Fact]
    public void Object_getOwnPropertyDescriptor_boxes_symbol_primitives()
        => AssertPassInBothModes(
            "built-ins/Object/getOwnPropertyDescriptor/primitive-symbol.js");

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-163.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-165.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-166.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-167.js")]
    public void RegExp_prototype_methods_have_standard_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.search/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/prop-desc.js")]
    public void RegExp_symbol_methods_have_standard_descriptors_in_interpreter(
        string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void RegExp_replace_callback_receiver_passes_in_interpreter()
        => AssertPass(
            "built-ins/String/prototype/replace/S15.5.4.11_A12.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/search/S15.5.4.12_A2_T3.js")]
    [InlineData("built-ins/String/prototype/search/S15.5.4.12_A2_T5.js")]
    public void Boxed_string_search_regressions_pass_in_interpreter(
        string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-176.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-177.js")]
    public void JSON_method_descriptors_preserve_callable_identity(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Object_assign_reports_its_spec_length()
        => AssertPassInBothModes("built-ins/Object/assign/assign-length.js");

    [Theory]
    [InlineData("built-ins/Object/assign/Target-Null.js")]
    [InlineData("built-ins/Object/assign/Target-Undefined.js")]
    public void Object_assign_rejects_nullish_targets(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/assign/OnlyOneArgument.js")]
    [InlineData("built-ins/Object/assign/Target-Boolean.js")]
    [InlineData("built-ins/Object/assign/Target-Number.js")]
    [InlineData("built-ins/Object/assign/Target-String.js")]
    public void Object_assign_boxes_primitive_targets(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/assign/Override.js")]
    [InlineData("built-ins/Object/assign/Override-notstringtarget.js")]
    [InlineData("built-ins/Object/assign/Source-String.js")]
    [InlineData("built-ins/Object/assign/source-non-enum.js")]
    public void Object_assign_copies_own_enumerable_source_properties(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Object_assign_throws_for_non_writable_target_properties()
        => AssertPassInBothModes("built-ins/Object/assign/target-set-not-writable.js");

    [Fact]
    public void Object_assign_rejects_writes_to_boxed_string_indices()
        => AssertPassInBothModes(
            "built-ins/Object/assign/assignment-to-readonly-property-of-target-must-throw-a-typeerror-exception.js");

    [Theory]
    [InlineData("built-ins/Object/assign/target-is-frozen-data-property-set-throws.js")]
    [InlineData("built-ins/Object/assign/target-is-non-extensible-existing-data-property.js")]
    [InlineData("built-ins/Object/assign/target-is-sealed-existing-data-property.js")]
    public void Object_assign_handles_symbol_keys_at_integrity_levels(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Object_constructor_reports_function_metadata_in_property_order()
        => AssertPassInBothModes("built-ins/Object/property-order.js");

    [Fact]
    public void Object_propertyIsEnumerable_accepts_symbol_keys()
        => AssertPassInBothModes(
            "built-ins/Object/prototype/propertyIsEnumerable/symbol_own_property.js");

    [Fact]
    public void Object_constructor_reports_length_one()
        => AssertPassInBothModes("built-ins/Object/S15.2.3_A3.js");

    [Fact]
    public void Object_constructor_owns_its_prototype_property()
        => AssertPassInBothModes("built-ins/Object/S15.2.3_A1.js");

    [Fact]
    public void Object_prototype_constructor_uses_the_realm_Object()
        => AssertPassInBothModes(
            "built-ins/Object/prototype/constructor/S15.2.4.1_A1_T1.js");

    [Fact]
    public void Object_constructor_prototype_is_non_configurable()
        => AssertPassInBothModes("built-ins/Object/prototype/S15.2.3.1_A3.js");

    [Fact]
    public void Object_prototype_accepts_its_existing_null_prototype()
        => AssertPassInBothModes(
            "built-ins/Object/prototype/setPrototypeOf-with-same-value.js");

    [Theory]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A1_T1.js")]
    [InlineData("built-ins/Array/prototype/unshift/S15.4.4.13_A1_T1.js")]
    public void Array_variadic_mutators_accept_zero_arguments(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/push/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/unshift/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A2_T3.js")]
    [InlineData("built-ins/Array/prototype/unshift/S15.4.4.13_A2_T3.js")]
    public void Array_empty_mutators_coerce_generic_receivers(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/pop/S15.4.4.6_A1.1_T1.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A1.1_T1.js")]
    public void Array_empty_removals_preserve_zero_length(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/includes/return-abrupt-get-length.js")]
    [InlineData("built-ins/Array/prototype/includes/return-abrupt-get-prop.js")]
    [InlineData("built-ins/Array/prototype/includes/return-abrupt-tonumber-length-symbol.js")]
    [InlineData("built-ins/Array/prototype/includes/return-abrupt-tonumber-length.js")]
    [InlineData("built-ins/Array/prototype/includes/values-are-not-cached.js")]
    public void Array_includes_observes_generic_property_access(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A2_T3.js")]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A2_T4.js")]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A2_T5.js")]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A3_T3.js")]
    public void Array_slice_supports_legacy_generic_receivers(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/slice/create-non-array-invalid-len.js")]
    [InlineData("built-ins/Array/prototype/slice/create-proxied-array-invalid-len.js")]
    public void Array_slice_rejects_unrepresentable_result_lengths(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A1.5_T1.js")]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A2.2_T5.js")]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A2_T6.js")]
    public void Array_slice_coerces_bounds_and_preserves_generic_values(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A2.1_T5.js")]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A3_T1.js")]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A3_T2.js")]
    [InlineData("built-ins/Array/prototype/slice/S15.4.4.10_A4_T1.js")]
    public void Array_slice_uses_generic_safe_integer_indexing(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/slice/15.4.4.10-10-c-ii-1.js")]
    [InlineData("built-ins/Array/prototype/slice/call-with-boolean.js")]
    public void Array_slice_creates_own_result_properties(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/toLocaleString/invoke-element-tolocalestring.js")]
    [InlineData("built-ins/Array/prototype/toLocaleString/primitive_this_value_getter.js")]
    [InlineData("built-ins/Array/prototype/toLocaleString/primitive_this_value.js")]
    public void Array_toLocaleString_invokes_element_methods(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/toReversed/this-value-boolean.js")]
    [InlineData("built-ins/Array/prototype/toSorted/this-value-boolean.js")]
    [InlineData("built-ins/Array/prototype/toSpliced/this-value-boolean.js")]
    [InlineData("built-ins/Array/prototype/with/this-value-boolean.js")]
    public void Array_copying_methods_box_boolean_receivers(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/toReversed/length-increased-while-iterating.js")]
    [InlineData("built-ins/Array/prototype/toSorted/length-increased-while-iterating.js")]
    [InlineData("built-ins/Array/prototype/toSpliced/length-increased-while-iterating.js")]
    [InlineData("built-ins/Array/prototype/with/length-increased-while-iterating.js")]
    public void Array_copying_methods_cache_source_length(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/toReversed/get-descending-order.js")]
    [InlineData("built-ins/Array/prototype/toReversed/length-decreased-while-iterating.js")]
    public void Array_toReversed_reads_captured_indices_in_descending_order(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_toSpliced_clamps_generic_length_before_deleting()
        => AssertPass(
            "built-ins/Array/prototype/toSpliced/length-clamped-to-2pow53minus1.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void JSON_stringify_calls_bigint_toJSON_before_replacer()
        => AssertPass(
            "built-ins/JSON/stringify/value-bigint-order.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/toReversed/length-exceeding-array-length-limit.js")]
    [InlineData("built-ins/Array/prototype/toSorted/length-exceeding-array-length-limit.js")]
    [InlineData("built-ins/Array/prototype/with/length-exceeding-array-length-limit.js")]
    public void Array_copying_methods_reject_oversized_results(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_with_propagates_index_coercion_errors()
        => AssertPassInBothModes(
            "built-ins/Array/prototype/with/index-throw-completion.js");

    [Theory]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-3-14.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-3-28.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-3-29.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-3-8.js")]
    [InlineData("built-ins/Array/prototype/map/create-non-array-invalid-len.js")]
    [InlineData("built-ins/Array/prototype/map/create-species-undef-invalid-len.js")]
    public void Array_map_rejects_unrepresentable_result_lengths(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_own_length_precedes_Array_prototype_length()
        => AssertPassInBothModes(
            "built-ins/Array/prototype/map/15.4.4.19-2-4.js");

    [Theory]
    [InlineData("built-ins/Array/prototype/filter/15.4.4.20-2-4.js")]
    [InlineData("built-ins/Array/prototype/filter/15.4.4.20-5-30.js")]
    public void Array_filter_uses_correct_length_and_default_this(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-2-4.js")]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-5-25.js")]
    public void Array_forEach_uses_correct_length_and_default_this(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_some_defaults_non_strict_callback_this_to_global()
        => AssertPassInBothModes(
            "built-ins/Array/prototype/some/15.4.4.17-5-25.js");

    [Theory]
    [InlineData("built-ins/Array/prototype/find/array-altered-during-loop.js")]
    [InlineData("built-ins/Array/prototype/find/return-abrupt-from-property.js")]
    public void Array_find_observes_live_generic_properties(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/findIndex/array-altered-during-loop.js")]
    [InlineData("built-ins/Array/prototype/findIndex/return-abrupt-from-property.js")]
    [InlineData("built-ins/Array/prototype/findIndex/return-abrupt-from-this-length.js")]
    public void Array_findIndex_observes_live_generic_properties(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_findLast_propagates_indexed_property_errors()
        => AssertPassInBothModes(
            "built-ins/Array/prototype/findLast/return-abrupt-from-property.js");

    [Theory]
    [InlineData("built-ins/Array/prototype/findLastIndex/return-abrupt-from-property.js")]
    [InlineData("built-ins/Array/prototype/findLastIndex/return-abrupt-from-this-length.js")]
    public void Array_findLastIndex_observes_live_generic_properties(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-2-4.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-2-4.js")]
    public void Array_reducers_prioritize_the_receiver_length(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/join/S15.4.4.5_A3.1_T2.js")]
    [InlineData("built-ins/Array/prototype/join/S15.4.4.5_A3.2_T2.js")]
    [InlineData("built-ins/Array/prototype/toString/S15.4.4.2_A1_T4.js")]
    public void Array_stringification_uses_string_hint_coercion(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_splice_coerces_delete_count_with_number_hint()
        => AssertPassInBothModes(
            "built-ins/Array/prototype/splice/S15.4.4.12_A2.2_T5.js");

    [Theory]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A2_T3.js")]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A2_T4.js")]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A3_T1.js")]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A3_T3.js")]
    public void Array_splice_mutates_generic_receivers(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A4_T1.js")]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A4_T2.js")]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A4_T3.js")]
    public void Array_splice_observes_inherited_array_indices(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/splice/length-and-deleteCount-exceeding-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/splice/length-exceeding-integer-limit-shrink-array.js")]
    public void Array_splice_supports_max_safe_generic_lengths(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/splice/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/splice/clamps-length-to-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/splice/length-near-integer-limit-grow-array.js")]
    [InlineData("built-ins/Array/prototype/splice/set_length_no_args.js")]
    [InlineData("built-ins/Array/prototype/splice/throws-if-integer-limit-exceeded.js")]
    public void Array_splice_handles_generic_length_edges(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/splice/S15.4.4.12_A6.1_T3.js")]
    [InlineData("built-ins/Array/prototype/splice/create-non-array-invalid-len.js")]
    [InlineData("built-ins/Array/prototype/splice/create-species-undef-invalid-len.js")]
    public void Array_splice_propagates_generic_creation_and_length_errors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/sort/S15.4.4.11_A3_T1.js")]
    [InlineData("built-ins/Array/prototype/sort/S15.4.4.11_A3_T2.js")]
    [InlineData("built-ins/Array/prototype/sort/S15.4.4.11_A4_T3.js")]
    [InlineData("built-ins/Array/prototype/sort/S15.4.4.11_A6_T2.js")]
    public void Array_sort_mutates_generic_receivers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/sort/comparefn-nonfunction-call-throws.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-getter-appends-elements.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-getter-decreases-length.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-getter-deletes-predecessor.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-getter-deletes-successor.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-getter-increases-length.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-getter-pops-elements.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-getter-sets-predecessor.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-getter-sets-successor.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-prototype-element.js")]
    public void Array_sort_observes_collection_side_effects(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_sort_boxes_primitive_receivers()
        => AssertPass(
            "built-ins/Array/prototype/sort/call-with-primitive.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/sort/precise-setter-appends-elements.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-setter-decreases-length.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-setter-deletes-predecessor.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-setter-deletes-successor.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-setter-increases-length.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-setter-pops-elements.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-setter-sets-predecessor.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-setter-sets-successor.js")]
    public void Array_sort_observes_writeback_side_effects(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/sort/stability-5-elements.js")]
    [InlineData("built-ins/Array/prototype/sort/stability-11-elements.js")]
    [InlineData("built-ins/Array/prototype/sort/stability-513-elements.js")]
    [InlineData("built-ins/Array/prototype/sort/stability-2048-elements.js")]
    public void Array_sort_is_stable_across_input_sizes(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/sort/precise-comparefn-throws.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-prototype-accessors.js")]
    public void Array_sort_observes_object_prototype_accessors(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/from/calling-from-valid-2.js")]
    [InlineData("built-ins/Array/from/iter-map-fn-this-arg.js")]
    public void Array_from_binds_the_mapping_this_argument(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/length/S15.4.2.2_A2.1_T1.js")]
    [InlineData("built-ins/Array/length/S15.4.4_A1.3_T1.js")]
    [InlineData("built-ins/Array/length/S15.4.5.1_A1.3_T1.js")]
    [InlineData("built-ins/Array/length/S15.4.5.1_A1.3_T2.js")]
    public void Array_lengths_follow_legacy_constructor_and_coercion_rules(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/length/define-own-prop-length-coercion-order.js")]
    [InlineData("built-ins/Array/length/define-own-prop-length-error.js")]
    [InlineData("built-ins/Array/length/define-own-prop-length-overflow-order.js")]
    public void Array_length_descriptors_validate_after_numeric_coercion(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_length_truncation_deletes_only_out_of_range_indices()
        => AssertPassInBothModes("built-ins/Array/S15.4.5.2_A3_T2.js");

    [Theory]
    [InlineData("built-ins/String/fromCodePoint/fromCodePoint.js")]
    [InlineData("built-ins/String/fromCodePoint/length.js")]
    public void String_fromCodePoint_exposes_standard_metadata(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/fromCharCode/S15.5.3.2_A1.js")]
    [InlineData("built-ins/String/fromCharCode/S9.7_A1.js")]
    [InlineData("built-ins/String/fromCharCode/S9.7_A2.1.js")]
    [InlineData("built-ins/String/fromCharCode/S9.7_A3.1_T4.js")]
    [InlineData("built-ins/String/fromCharCode/touint16-tonumber-throws-bigint.js")]
    public void String_fromCharCode_applies_ToUint16(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/S15.5.3.1_A1.js")]
    [InlineData("built-ins/String/prototype/S15.5.3.1_A3.js")]
    [InlineData("built-ins/String/prototype/S15.5.3.1_A4.js")]
    public void String_constructor_owns_an_immutable_prototype(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/S15.5.4_A2.js")]
    [InlineData("built-ins/String/prototype/S15.5.4_A3.js")]
    public void String_prototype_has_its_intrinsic_object_shape(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void String_toString_rejects_non_string_receivers()
        => AssertPassInBothModes(
            "built-ins/String/prototype/toString/non-generic.js");

    [Fact]
    public void Interpreted_String_valueOf_rejects_non_string_receivers()
        => AssertPass(
            "built-ins/String/prototype/valueOf/non-generic.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_indexOf_propagates_search_string_coercion_errors()
        => AssertPassInBothModes(
            "built-ins/String/prototype/indexOf/searchstring-tostring-errors.js");

    [Theory]
    [InlineData("built-ins/String/prototype/indexOf/position-tointeger.js")]
    [InlineData("built-ins/String/prototype/indexOf/S15.5.4.7_A1_T10.js")]
    [InlineData("built-ins/String/prototype/indexOf/S15.5.4.7_A4_T1.js")]
    [InlineData("built-ins/String/prototype/indexOf/S15.5.4.7_A4_T2.js")]
    [InlineData("built-ins/String/prototype/indexOf/S15.5.4.7_A4_T3.js")]
    [InlineData("built-ins/String/prototype/indexOf/S15.5.4.7_A4_T4.js")]
    [InlineData("built-ins/String/prototype/indexOf/S15.5.4.7_A4_T5.js")]
    [InlineData("built-ins/String/prototype/substring/S15.5.4.15_A1_T15.js")]
    [InlineData("built-ins/String/prototype/substring/S15.5.4.15_A2_T1.js")]
    [InlineData("built-ins/String/prototype/substring/S15.5.4.15_A1_T2.js")]
    [InlineData("built-ins/String/prototype/slice/S15.5.4.13_A1_T15.js")]
    [InlineData("built-ins/String/prototype/slice/S15.5.4.13_A2_T1.js")]
    [InlineData("built-ins/String/prototype/slice/S15.5.4.13_A1_T2.js")]
    public void Compiled_string_search_and_substring_use_Javascript_coercion(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/substring/S15.5.4.15_A1_T5.js")]
    [InlineData("built-ins/String/prototype/substring/S15.5.4.15_A3_T1.js")]
    [InlineData("built-ins/String/prototype/substring/S15.5.4.15_A3_T2.js")]
    [InlineData("built-ins/String/prototype/substring/S15.5.4.15_A3_T3.js")]
    [InlineData("built-ins/String/prototype/substring/S15.5.4.15_A3_T4.js")]
    [InlineData("built-ins/String/prototype/substring/this-value-tostring-throws-toprimitive.js")]
    public void String_substring_preserves_generic_receiver_coercion(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Error/error-message-tostring-toprimitive.js")]
    [InlineData("built-ins/Error/prototype/toString/tostring-message-throws-toprimitive.js")]
    [InlineData("built-ins/String/prototype/replace/tostring-this-throws-toprimitive.js")]
    [InlineData("built-ins/String/prototype/slice/this-value-tostring-throws-toprimitive.js")]
    [InlineData("built-ins/String/prototype/toLowerCase/this-value-tostring-throws-toprimitive.js")]
    [InlineData("built-ins/String/prototype/trimEnd/this-value-object-cannot-convert-to-primitive-err.js")]
    [InlineData("built-ins/String/prototype/trimStart/this-value-object-cannot-convert-to-primitive-err.js")]
    public void String_coercion_rejects_explicitly_unusable_primitive_methods(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/prop-desc.js")]
    [InlineData("built-ins/Promise/allSettled/prop-desc.js")]
    [InlineData("built-ins/Promise/any/prop-desc.js")]
    [InlineData("built-ins/Promise/race/prop-desc.js")]
    [InlineData("built-ins/Promise/reject/prop-desc.js")]
    [InlineData("built-ins/Promise/resolve/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.search/prop-desc.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/prop-desc.js")]
    [InlineData("built-ins/Math/f16round/length.js")]
    [InlineData("built-ins/Math/f16round/name.js")]
    [InlineData("built-ins/Math/f16round/not-a-constructor.js")]
    [InlineData("built-ins/Math/f16round/prop-desc.js")]
    [InlineData("built-ins/Math/sumPrecise/length.js")]
    [InlineData("built-ins/Math/sumPrecise/name.js")]
    [InlineData("built-ins/Math/sumPrecise/not-a-constructor.js")]
    [InlineData("built-ins/Math/sumPrecise/prop-desc.js")]
    [InlineData("built-ins/Error/isError/bigints.js")]
    [InlineData("built-ins/Error/isError/errors.js")]
    [InlineData("built-ins/Error/isError/fake-errors.js")]
    [InlineData("built-ins/Error/isError/is-a-constructor.js")]
    [InlineData("built-ins/Error/isError/name.js")]
    [InlineData("built-ins/Error/isError/non-error-objects.js")]
    [InlineData("built-ins/Error/isError/primitives.js")]
    [InlineData("built-ins/Error/isError/prop-desc.js")]
    [InlineData("built-ins/Error/isError/symbols.js")]
    public void Compiled_builtin_static_metadata_and_error_branding_match_the_spec(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/toLowerCase/S15.5.4.16_A1_T6.js")]
    [InlineData("built-ins/String/prototype/toLowerCase/S15.5.4.16_A1_T7.js")]
    [InlineData("built-ins/String/prototype/toLowerCase/S15.5.4.16_A1_T8.js")]
    [InlineData("built-ins/String/prototype/toLocaleLowerCase/S15.5.4.17_A1_T6.js")]
    [InlineData("built-ins/String/prototype/toLocaleLowerCase/S15.5.4.17_A1_T7.js")]
    [InlineData("built-ins/String/prototype/toLocaleLowerCase/S15.5.4.17_A1_T8.js")]
    [InlineData("built-ins/String/prototype/toUpperCase/S15.5.4.18_A1_T6.js")]
    [InlineData("built-ins/String/prototype/toUpperCase/S15.5.4.18_A1_T7.js")]
    [InlineData("built-ins/String/prototype/toUpperCase/S15.5.4.18_A1_T8.js")]
    [InlineData("built-ins/String/prototype/toLocaleUpperCase/S15.5.4.19_A1_T6.js")]
    [InlineData("built-ins/String/prototype/toLocaleUpperCase/S15.5.4.19_A1_T7.js")]
    [InlineData("built-ins/String/prototype/toLocaleUpperCase/S15.5.4.19_A1_T8.js")]
    public void String_case_methods_coerce_numeric_receivers(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/isWellFormed/length.js")]
    [InlineData("built-ins/String/prototype/isWellFormed/name.js")]
    [InlineData("built-ins/String/prototype/isWellFormed/not-a-constructor.js")]
    [InlineData("built-ins/String/prototype/isWellFormed/prop-desc.js")]
    [InlineData("built-ins/String/prototype/isWellFormed/return-abrupt-from-this.js")]
    [InlineData("built-ins/String/prototype/isWellFormed/returns-boolean.js")]
    [InlineData("built-ins/String/prototype/isWellFormed/to-string.js")]
    public void String_isWellFormed_supports_unicode_and_coercion(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/toWellFormed/length.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/name.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/not-a-constructor.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/prop-desc.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/return-abrupt-from-this.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/returns-well-formed-string.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/to-string.js")]
    public void String_toWellFormed_replaces_unpaired_surrogates(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/length.js")]
    [InlineData("built-ins/BigInt/name.js")]
    [InlineData("built-ins/BigInt/constructor-integer.js")]
    [InlineData("built-ins/BigInt/constructor-from-decimal-string.js")]
    public void BigInt_is_available_as_a_global_function(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/prototype/constructor.js")]
    public void BigInt_prototype_constructor_has_ordinary_descriptor(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/isWellFormed/to-string-primitive.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/to-string-primitive.js")]
    public void String_well_formed_methods_ignore_primitive_prototype_overrides(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Symbol/prototype/constructor.js")]
    public void Symbol_prototype_constructor_has_ordinary_descriptor(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/symbol-data-property-default-non-strict.js")]
    [InlineData("built-ins/Object/defineProperty/symbol-data-property-default-strict.js")]
    [InlineData("built-ins/Object/defineProperty/symbol-data-property-writable.js")]
    [InlineData("built-ins/Array/prototype/concat/is-concat-spreadable-get-err.js")]
    public void Compiled_symbol_descriptors_preserve_property_semantics(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Symbol/prototype/toString/length.js")]
    [InlineData("built-ins/Symbol/prototype/toString/name.js")]
    [InlineData("built-ins/Symbol/prototype/toString/not-a-constructor.js")]
    [InlineData("built-ins/Symbol/prototype/toString/prop-desc.js")]
    [InlineData("built-ins/Symbol/prototype/toString/toString.js")]
    [InlineData("built-ins/Symbol/prototype/toString/undefined.js")]
    public void Symbol_prototype_toString_is_callable(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Symbol/prototype/valueOf/length.js")]
    [InlineData("built-ins/Symbol/prototype/valueOf/name.js")]
    [InlineData("built-ins/Symbol/prototype/valueOf/not-a-constructor.js")]
    [InlineData("built-ins/Symbol/prototype/valueOf/prop-desc.js")]
    [InlineData("built-ins/Symbol/prototype/valueOf/this-val-non-obj.js")]
    [InlineData("built-ins/Symbol/prototype/valueOf/this-val-obj-non-symbol.js")]
    [InlineData("built-ins/Symbol/prototype/valueOf/this-val-obj-symbol.js")]
    [InlineData("built-ins/Symbol/prototype/valueOf/this-val-symbol.js")]
    public void Symbol_prototype_valueOf_checks_receiver_brand(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/prototype/valueOf/length.js")]
    [InlineData("built-ins/BigInt/prototype/valueOf/name.js")]
    [InlineData("built-ins/BigInt/prototype/valueOf/not-a-constructor.js")]
    [InlineData("built-ins/BigInt/prototype/valueOf/prop-desc.js")]
    [InlineData("built-ins/BigInt/prototype/valueOf/return.js")]
    [InlineData("built-ins/BigInt/prototype/valueOf/this-value-invalid-object-throws.js")]
    [InlineData("built-ins/BigInt/prototype/valueOf/this-value-invalid-primitive-throws.js")]
    public void BigInt_prototype_valueOf_checks_receiver_brand(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/prototype/toString/length.js")]
    [InlineData("built-ins/BigInt/prototype/toString/name.js")]
    [InlineData("built-ins/BigInt/prototype/toString/not-a-constructor.js")]
    [InlineData("built-ins/BigInt/prototype/toString/prop-desc.js")]
    [InlineData("built-ins/BigInt/prototype/toString/default-radix.js")]
    [InlineData("built-ins/BigInt/prototype/toString/prototype-call.js")]
    [InlineData("built-ins/BigInt/prototype/toString/string-is-code-units-of-decimal-digits-only.js")]
    [InlineData("built-ins/BigInt/prototype/toString/radix-2-to-36.js")]
    public void BigInt_prototype_toString_formats_radices(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("language/expressions/less-than/bigint-and-number.js")]
    [InlineData("language/expressions/less-than-or-equal/bigint-and-number.js")]
    [InlineData("language/expressions/greater-than/bigint-and-number.js")]
    [InlineData("language/expressions/greater-than-or-equal/bigint-and-number.js")]
    public void BigInt_relational_comparison_accepts_numbers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("language/expressions/prefix-increment/bigint.js")]
    [InlineData("language/expressions/prefix-decrement/bigint.js")]
    [InlineData("language/expressions/postfix-increment/bigint.js")]
    [InlineData("language/expressions/postfix-decrement/bigint.js")]
    [InlineData("built-ins/BigInt/prototype/toString/a-z.js")]
    public void BigInt_update_operators_preserve_bigint_values(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/prototype/toString/radix-err.js")]
    [InlineData("built-ins/BigInt/prototype/toString/radix-tointegerorinfinity-throws-symbol.js")]
    [InlineData("built-ins/BigInt/prototype/toString/radix-tointegerorinfinity-throws-toprimitive-or-bigint.js")]
    public void BigInt_toString_coerces_and_validates_radix(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/raw/return-the-string-value.js")]
    [InlineData("built-ins/String/raw/template-not-object-throws.js")]
    [InlineData("built-ins/String/raw/template-raw-not-object-throws.js")]
    [InlineData("built-ins/String/raw/returns-abrupt-from-substitution-symbol.js")]
    [InlineData("built-ins/String/raw/substitutions-are-limited-to-template-raw-length.js")]
    public void String_raw_reads_generic_template_objects(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/raw/nextkey-is-symbol-throws.js")]
    [InlineData("built-ins/String/raw/raw.js")]
    [InlineData("built-ins/String/raw/returns-abrupt-from-next-key-toString.js")]
    [InlineData("built-ins/String/raw/returns-abrupt-from-next-key.js")]
    [InlineData("built-ins/String/raw/returns-abrupt-from-substitution.js")]
    [InlineData("built-ins/String/raw/template-length-is-symbol-throws.js")]
    [InlineData("built-ins/String/raw/template-length-throws.js")]
    [InlineData("built-ins/String/raw/template-raw-throws.js")]
    public void String_raw_propagates_observable_coercions(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/raw/special-characters.js")]
    public void String_raw_normalizes_source_line_terminators(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replace/S15.5.4.11_A1_T9.js")]
    [InlineData("built-ins/String/prototype/replace/S15.5.4.11_A1_T10.js")]
    [InlineData("built-ins/String/prototype/replace/S15.5.4.11_A1_T11.js")]
    [InlineData("built-ins/String/prototype/replace/S15.5.4.11_A1_T12.js")]
    [InlineData("built-ins/String/prototype/replace/replaceValue-evaluation-order.js")]
    public void String_replace_supports_functional_plain_search(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replace/S15.5.4.11_A4_T1.js")]
    [InlineData("built-ins/String/prototype/replace/S15.5.4.11_A4_T2.js")]
    [InlineData("built-ins/String/prototype/replace/S15.5.4.11_A4_T3.js")]
    [InlineData("built-ins/String/prototype/replace/S15.5.4.11_A4_T4.js")]
    public void String_replace_passes_regexp_captures_to_replacer(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replace/cstm-replace-get-err.js")]
    [InlineData("built-ins/String/prototype/replace/cstm-replace-invocation.js")]
    [InlineData("built-ins/String/prototype/replace/cstm-replace-is-null.js")]
    public void String_replace_honors_custom_symbol_protocol(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/subst-after.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/subst-before.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/subst-capture-idx-1.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/subst-capture-idx-2.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/subst-dollar.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/subst-matched.js")]
    public void RegExp_replace_expands_replacement_substitutions(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/fn-invoke-this-no-strict.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/fn-invoke-this-strict.js")]
    public void RegExp_replace_uses_undefined_callback_receiver(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-coerce-index-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-coerce-index-undefined.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-coerce-index.js")]
    public void RegExp_replace_coerces_custom_match_indices(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-coerce-capture-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-coerce-capture.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-coerce-length-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-coerce-length.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-get-capture-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-get-length-err.js")]
    public void RegExp_replace_reads_array_like_captures(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-coerce-matched-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-coerce-matched-global.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/result-coerce-matched.js")]
    public void RegExp_replace_coerces_custom_match_values(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/get-global-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/get-unicode-error.js")]
    public void RegExp_replace_observes_flag_access(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    public static TheoryData<string> CompiledRegExpReplaceProtocolCases => new()
    {
        "built-ins/RegExp/prototype/Symbol.replace/coerce-global.js",
        "built-ins/RegExp/prototype/Symbol.replace/coerce-lastindex-err.js",
        "built-ins/RegExp/prototype/Symbol.replace/coerce-unicode.js",
        "built-ins/RegExp/prototype/Symbol.replace/fn-invoke-args-empty-result.js",
        "built-ins/RegExp/prototype/Symbol.replace/g-init-lastindex-err.js",
        "built-ins/RegExp/prototype/Symbol.replace/g-pos-decrement.js",
        "built-ins/RegExp/prototype/Symbol.replace/g-pos-increment.js",
        "built-ins/RegExp/prototype/Symbol.replace/get-exec-err.js",
        "built-ins/RegExp/prototype/Symbol.replace/result-coerce-capture-err.js",
        "built-ins/RegExp/prototype/Symbol.replace/result-coerce-capture.js",
        "built-ins/RegExp/prototype/Symbol.replace/result-coerce-index-undefined.js",
        "built-ins/RegExp/prototype/Symbol.replace/result-coerce-index.js",
        "built-ins/RegExp/prototype/Symbol.replace/result-coerce-length-err.js",
        "built-ins/RegExp/prototype/Symbol.replace/result-coerce-length.js",
        "built-ins/RegExp/prototype/Symbol.replace/result-coerce-matched-global.js",
        "built-ins/RegExp/prototype/Symbol.replace/result-coerce-matched.js",
        "built-ins/RegExp/prototype/Symbol.replace/result-get-capture-err.js",
        "built-ins/RegExp/prototype/Symbol.replace/result-get-length-err.js",
        "built-ins/RegExp/prototype/Symbol.replace/u-advance-after-empty.js",
        "built-ins/RegExp/prototype/Symbol.replace/y-fail-global-return.js",
        "built-ins/RegExp/prototype/Symbol.replace/subst-capture-idx-2.js",
    };

    [Theory]
    [MemberData(nameof(CompiledRegExpReplaceProtocolCases))]
    public void RegExp_replace_follows_exec_result_protocol_when_compiled(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/BigInt/asIntN/arithmetic.js")]
    [InlineData("built-ins/BigInt/asIntN/length.js")]
    [InlineData("built-ins/BigInt/asIntN/name.js")]
    [InlineData("built-ins/BigInt/asIntN/not-a-constructor.js")]
    [InlineData("built-ins/BigInt/asUintN/arithmetic.js")]
    [InlineData("built-ins/BigInt/asUintN/length.js")]
    [InlineData("built-ins/BigInt/asUintN/name.js")]
    [InlineData("built-ins/BigInt/asUintN/not-a-constructor.js")]
    public void BigInt_fixed_width_statics_truncate_values(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/asIntN/bits-toindex.js")]
    [InlineData("built-ins/BigInt/asUintN/bits-toindex.js")]
    public void BigInt_fixed_width_statics_coerce_bit_width(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/asIntN/bigint-tobigint.js")]
    [InlineData("built-ins/BigInt/asUintN/bigint-tobigint.js")]
    public void BigInt_fixed_width_statics_coerce_bigint_values(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/asIntN/bigint-tobigint-wrapped-values.js")]
    [InlineData("built-ins/BigInt/asIntN/bits-toindex-wrapped-values.js")]
    [InlineData("built-ins/BigInt/asUintN/bigint-tobigint-wrapped-values.js")]
    [InlineData("built-ins/BigInt/asUintN/bits-toindex-wrapped-values.js")]
    public void BigInt_fixed_width_statics_unbox_wrapped_values(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/asIntN/bigint-tobigint-toprimitive.js")]
    [InlineData("built-ins/BigInt/asIntN/bits-toindex-toprimitive.js")]
    [InlineData("built-ins/BigInt/asIntN/order-of-steps.js")]
    [InlineData("built-ins/BigInt/asUintN/bigint-tobigint-toprimitive.js")]
    [InlineData("built-ins/BigInt/asUintN/bits-toindex-toprimitive.js")]
    [InlineData("built-ins/BigInt/asUintN/order-of-steps.js")]
    public void BigInt_fixed_width_statics_observe_primitive_conversion(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/asIntN/bigint-tobigint-errors.js")]
    [InlineData("built-ins/BigInt/asIntN/bits-toindex-errors.js")]
    [InlineData("built-ins/BigInt/asUintN/bigint-tobigint-errors.js")]
    [InlineData("built-ins/BigInt/asUintN/bits-toindex-errors.js")]
    public void BigInt_fixed_width_statics_reject_invalid_operands(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/constructor-empty-string.js")]
    [InlineData("built-ins/BigInt/constructor-from-binary-string.js")]
    [InlineData("built-ins/BigInt/constructor-from-hex-string.js")]
    [InlineData("built-ins/BigInt/constructor-from-octal-string.js")]
    [InlineData("built-ins/BigInt/constructor-trailing-leading-spaces.js")]
    public void BigInt_constructor_parses_integer_string_grammar(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/constructor-coercion.js")]
    public void BigInt_constructor_uses_abstract_conversion(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/infinity-throws-rangeerror.js")]
    [InlineData("built-ins/BigInt/nan-throws-rangeerror.js")]
    [InlineData("built-ins/BigInt/negative-infinity-throws.rangeerror.js")]
    [InlineData("built-ins/BigInt/non-integer-rangeerror.js")]
    public void BigInt_constructor_rejects_non_integer_numbers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/call-value-of-when-to-string-present.js")]
    [InlineData("built-ins/BigInt/tostring-throws.js")]
    [InlineData("built-ins/BigInt/valueof-throws.js")]
    public void BigInt_constructor_observes_object_conversion(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/BigInt/prototype/toLocaleString/not-a-constructor.js")]
    public void BigInt_prototype_toLocaleString_is_not_a_constructor(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-call-abrupt.js")]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-call-each-match-position.js")]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-call-matching-empty.js")]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-call-tostring-abrupt.js")]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-fn-skip-toString.js")]
    public void String_replaceAll_calls_functional_replacers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024-0x0024.js")]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024-0x0026.js")]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024-0x0027.js")]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024-0x0060.js")]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024.js")]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024N.js")]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024NN.js")]
    public void String_replaceAll_expands_replacement_substitutions(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-replacer-is-null.js")]
    public void String_replaceAll_honors_custom_symbol_protocol(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-value-tostring.js")]
    public void String_replaceAll_observes_string_coercion(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024-0x003C.js")]
    public void String_replaceAll_preserves_named_capture_tokens(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-replacer-call-abrupt.js")]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-replacer-call.js")]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-replacer-method-abrupt.js")]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-replacer-before-tostring.js")]
    public void String_replaceAll_preserves_borrowed_receiver_for_symbol_hook(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replaceAll/this-tostring.js")]
    public void String_replaceAll_stringifies_borrowed_receivers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/replaceAll/cstm-replaceall-on-boolean-primitive.js")]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-replacer-before-tostring.js")]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-replacer-call-abrupt.js")]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-replacer-method-abrupt.js")]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-tostring-regexp.js")]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-flags-no-g-throws.js")]
    [InlineData("built-ins/String/prototype/replaceAll/searchValue-tostring-abrupt.js")]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-tostring-abrupt.js")]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024-0x003C.js")]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024N.js")]
    [InlineData("built-ins/String/prototype/replaceAll/getSubstitution-0x0024NN.js")]
    public void String_replaceAll_preserves_symbol_dispatch_before_coercion(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-invocation.js")]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-is-null.js")]
    [InlineData("built-ins/String/prototype/match/invoke-builtin-match.js")]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-get-err.js")]
    public void String_match_invokes_symbol_protocol(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-on-boolean-primitive.js")]
    [InlineData("built-ins/String/prototype/match/S15.5.4.10_A1_T11.js")]
    public void String_match_preserves_primitive_fallback_coercion(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-on-bigint-primitive.js")]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-on-boolean-primitive.js")]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-on-number-primitive.js")]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-on-string-primitive.js")]
    public void String_match_ignores_symbol_hooks_on_primitive_patterns(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/search/cstm-search-invocation.js")]
    [InlineData("built-ins/String/prototype/search/cstm-search-is-null.js")]
    [InlineData("built-ins/String/prototype/search/invoke-builtin-search.js")]
    [InlineData("built-ins/String/prototype/search/invoke-builtin-search-searcher-undef.js")]
    [InlineData("built-ins/String/prototype/search/cstm-search-get-err.js")]
    public void String_search_invokes_symbol_protocol(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/search/cstm-search-on-bigint-primitive.js")]
    [InlineData("built-ins/String/prototype/search/cstm-search-on-boolean-primitive.js")]
    [InlineData("built-ins/String/prototype/search/cstm-search-on-number-primitive.js")]
    [InlineData("built-ins/String/prototype/search/cstm-search-on-string-primitive.js")]
    public void String_search_ignores_symbol_hooks_on_primitive_patterns(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/split/cstm-split-invocation.js")]
    [InlineData("built-ins/String/prototype/split/cstm-split-is-null.js")]
    [InlineData("built-ins/String/prototype/split/cstm-split-get-err.js")]
    public void String_split_invokes_symbol_protocol(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/split/cstm-split-on-bigint-primitive.js")]
    [InlineData("built-ins/String/prototype/split/cstm-split-on-boolean-primitive.js")]
    [InlineData("built-ins/String/prototype/split/cstm-split-on-number-primitive.js")]
    [InlineData("built-ins/String/prototype/split/cstm-split-on-string-primitive.js")]
    public void String_split_ignores_symbol_hooks_on_primitive_separators(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/matchAll/regexp-matchAll-invocation.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-prototype-matchAll-invocation.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-matchAll-not-callable.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-matchAll-is-undefined-or-null.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-prototype-matchAll-throws.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-matchAll-throws.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-get-matchAll-throws.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-prototype-get-matchAll-throws.js")]
    public void String_matchAll_invokes_symbol_protocol(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/matchAll/cstm-matchall-on-bigint-primitive.js")]
    [InlineData("built-ins/String/prototype/matchAll/cstm-matchall-on-boolean-primitive.js")]
    [InlineData("built-ins/String/prototype/matchAll/cstm-matchall-on-number-primitive.js")]
    [InlineData("built-ins/String/prototype/matchAll/cstm-matchall-on-string-primitive.js")]
    public void String_matchAll_ignores_symbol_hooks_on_primitive_patterns(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/matchAll/regexp-is-null.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-is-undefined.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-is-undefined-or-null-invokes-matchAll.js")]
    public void String_matchAll_handles_nullish_patterns(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/includes/searchstring-is-regexp-throws.js")]
    public void String_includes_rejects_regexp_search_strings(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/startsWith/searchstring-is-regexp-throws.js")]
    public void String_startsWith_rejects_regexp_search_strings(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/endsWith/searchstring-is-regexp-throws.js")]
    public void String_endsWith_rejects_regexp_search_strings(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/flat/array-like-objects.js")]
    [InlineData("built-ins/Array/prototype/flatMap/array-like-objects-nested.js")]
    [InlineData("built-ins/Array/prototype/flatMap/array-like-objects-poisoned-length.js")]
    [InlineData("built-ins/Array/prototype/flatMap/this-value-null-undefined-throws.js")]
    public void Array_flattening_methods_support_generic_receivers(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Array_with_does_not_read_the_replaced_index()
        => AssertPassInBothModes(
            "built-ins/Array/prototype/with/no-get-replaced-index.js");

    [Theory]
    [InlineData("built-ins/Error/constructor.js")]
    [InlineData("built-ins/Error/error-message-tostring-symbol.js")]
    public void Error_constructor_arguments_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Error_prototype_property_is_nonconfigurable()
        => AssertPassInBothModes(
            "built-ins/Error/prototype/S15.11.3.1_A1_T1.js");

    [Fact]
    public void Error_instances_inherit_from_Error_prototype()
        => AssertPassInBothModes("built-ins/Error/instance-prototype.js");

    [Fact]
    public void Error_toString_unbound_call_uses_undefined_receiver()
        => AssertPassInBothModes(
            "built-ins/Error/prototype/toString/called-as-function.js");

    [Theory]
    [InlineData("built-ins/JSON/parse/length.js")]
    [InlineData("built-ins/JSON/parse/prop-desc.js")]
    [InlineData("built-ins/JSON/stringify/length.js")]
    [InlineData("built-ins/JSON/stringify/prop-desc.js")]
    public void JSON_method_metadata_matches_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-5.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-6.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-7.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-8.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-116.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-178.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-179.js")]
    public void Legacy_global_descriptors_remain_supported(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Promise/all/invoke-resolve-error-reject.js")]
    [InlineData("built-ins/Promise/allSettled/invoke-resolve-error-reject.js")]
    [InlineData("built-ins/Promise/any/invoke-resolve-error-reject.js")]
    public void Promise_combinators_reject_when_resolve_throws(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Promise_race_rejects_when_resolve_throws()
        => AssertPass(
            "built-ins/Promise/race/invoke-resolve-error-reject.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Promise/prototype/catch/prop-desc.js")]
    [InlineData("built-ins/Promise/prototype/then/prop-desc.js")]
    [InlineData("built-ins/Promise/prototype/prop-desc.js")]
    public void Promise_prototype_descriptors_match_the_spec(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Promise_resolve_reports_its_spec_length()
        => AssertPass(
            "built-ins/Promise/resolve/length.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Promise/resolve/S25.4.4.5_A2.1_T1.js")]
    [InlineData("built-ins/Promise/resolve/S25.4.4.5_A2.2_T1.js")]
    [InlineData("built-ins/Promise/resolve/S25.4.4.5_A2.3_T1.js")]
    public void Promise_resolve_preserves_same_constructor_identity(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/reject-function-nonconstructor.js")]
    [InlineData("built-ins/Promise/resolve-function-nonconstructor.js")]
    public void Promise_capability_callbacks_are_not_constructors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Promise_capability_executor_is_extensible()
        => AssertPassInBothModes("built-ins/Promise/executor-function-extensible.js");

    [Theory]
    [InlineData("built-ins/Promise/all/resolve-non-callable.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-non-callable.js")]
    [InlineData("built-ins/Promise/any/resolve-non-callable.js")]
    [InlineData("built-ins/Promise/race/resolve-non-callable.js")]
    public void Promise_combinators_validate_resolve_before_iteration(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/invoke-resolve.js")]
    [InlineData("built-ins/Promise/allSettled/invoke-resolve.js")]
    [InlineData("built-ins/Promise/race/invoke-resolve.js")]
    public void Promise_combinators_invoke_constructor_resolve(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Promise_any_invokes_constructor_resolve()
        => AssertPassInBothModes("built-ins/Promise/any/invoke-resolve.js");

    [Theory]
    [InlineData("built-ins/Promise/all/iter-arg-is-string-resolve.js")]
    [InlineData("built-ins/Promise/allSettled/iter-arg-is-string-resolve.js")]
    [InlineData("built-ins/Promise/any/iter-arg-is-empty-string-reject.js")]
    [InlineData("built-ins/Promise/race/invoke-resolve-error-reject.js")]
    [InlineData("built-ins/Promise/race/iter-arg-is-string-resolve.js")]
    public void Promise_combinators_consume_string_iterables_in_resolve_order(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/iter-assigned-undefined-reject.js")]
    [InlineData("built-ins/Promise/allSettled/iter-assigned-undefined-reject.js")]
    [InlineData("built-ins/Promise/any/iter-assigned-undefined-reject.js")]
    [InlineData("built-ins/Promise/race/iter-assigned-undefined-reject.js")]
    public void Promise_combinators_reject_objects_without_callable_iterators(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/allSettled/returns-promise.js")]
    [InlineData("built-ins/Promise/any/returns-promise.js")]
    public void Promise_combinators_return_objects_with_the_Promise_prototype(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/resolve-element-function-extensible.js")]
    [InlineData("built-ins/Promise/allSettled/reject-element-function-extensible.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-element-function-extensible.js")]
    [InlineData("built-ins/Promise/any/reject-element-function-extensible.js")]
    public void Promise_combinator_element_callbacks_are_extensible(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/reject-via-abrupt.js")]
    [InlineData("built-ins/Promise/reject-via-abrupt-queue.js")]
    public void Promise_executor_preserves_thrown_rejection_values(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Promise_constructor_validates_executor_before_new_target_prototype()
        => AssertPassInBothModes(
            "built-ins/Promise/get-prototype-abrupt-executor-not-callable.js");

    [Theory]
    [InlineData("built-ins/String/S8.12.8_A1.js")]
    [InlineData("built-ins/String/S9.8_A5_T1.js")]
    public void String_legacy_coercion_matches_compiled_mode(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Bound_String_match_retains_its_receiver()
        => AssertPassInBothModes(
            "built-ins/String/prototype/match/S15.5.4.10_A1_T3.js");

    [Theory]
    [InlineData("language/expressions/call/spread-mult-obj-null.js")]
    [InlineData("language/expressions/call/spread-mult-obj-undefined.js")]
    [InlineData("language/expressions/call/spread-obj-null.js")]
    [InlineData("language/expressions/call/spread-obj-undefined.js")]
    public void Object_spread_ignores_nullish_sources(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("language/expressions/new/ctorExpr-isCtor-after-args-eval-fn-wrapup.js")]
    [InlineData("language/expressions/new/ctorExpr-isCtor-after-args-eval.js")]
    public void New_evaluates_arguments_before_constructor_validation(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("language/expressions/new/spread-err-mult-err-iter-get-value.js")]
    [InlineData("language/expressions/new/spread-err-sngl-err-itr-get-value.js")]
    public void New_spread_rejects_invalid_iterators(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/iter-assigned-null-reject.js")]
    [InlineData("built-ins/Promise/all/iter-returns-null-reject.js")]
    [InlineData("built-ins/Promise/all/S25.4.4.1_A3.1_T3.js")]
    [InlineData("built-ins/Promise/race/iter-assigned-null-reject.js")]
    [InlineData("built-ins/Promise/race/iter-returns-null-reject.js")]
    [InlineData("built-ins/Promise/race/S25.4.4.3_A2.2_T3.js")]
    public void Promise_combinators_preserve_iterator_error_values(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Promise_any_does_not_read_constructor_species()
        => AssertPassInBothModes("built-ins/Promise/any/species-get-error.js");

    [Theory]
    [InlineData("built-ins/Promise/allKeyed/arg-is-function.js")]
    [InlineData("built-ins/Promise/allKeyed/arg-not-object-reject-bigint.js")]
    [InlineData("built-ins/Promise/allKeyed/ctx-non-ctor.js")]
    [InlineData("built-ins/Promise/allKeyed/extensible.js")]
    [InlineData("built-ins/Promise/allKeyed/key-order-preserved.js")]
    [InlineData("built-ins/Promise/allKeyed/length.js")]
    [InlineData("built-ins/Promise/allKeyed/name.js")]
    [InlineData("built-ins/Promise/allKeyed/non-enumerable-properties-ignored.js")]
    [InlineData("built-ins/Promise/allKeyed/not-a-constructor.js")]
    [InlineData("built-ins/Promise/allKeyed/prop-desc.js")]
    [InlineData("built-ins/Promise/allKeyed/proto.js")]
    [InlineData("built-ins/Promise/allKeyed/prototype-keys-ignored.js")]
    [InlineData("built-ins/Promise/allKeyed/reject-deferred.js")]
    [InlineData("built-ins/Promise/allKeyed/reject-immed.js")]
    [InlineData("built-ins/Promise/allKeyed/resolve-not-callable-reject-with-typeerror.js")]
    [InlineData("built-ins/Promise/allKeyed/resolves-empty-object.js")]
    [InlineData("built-ins/Promise/allKeyed/symbol-keys.js")]
    public void Promise_allKeyed_resolves_own_enumerable_properties(string relativePath)
        => AssertPromiseKeyedPass(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/allSettledKeyed/arg-is-function.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/arg-not-object-reject-bigint.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/ctx-non-ctor.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/extensible.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/key-order-preserved.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/length.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/name.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/non-enumerable-properties-ignored.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/not-a-constructor.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/prop-desc.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/proto.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/prototype-keys-ignored.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/resolve-not-callable-reject-with-typeerror.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/resolved-all-fulfilled.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/resolved-all-mixed.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/resolved-all-rejected.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/resolves-empty-object.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/symbol-keys.js")]
    public void Promise_allSettledKeyed_retains_keyed_outcomes(string relativePath)
        => AssertPromiseKeyedPass(relativePath);

    // These two exercise an independent compiled nested-function capture gap
    // in asyncHelpers' local `check` helper. Keep interpreter coverage here;
    // the keyed combinators' primitive-rejection behavior is covered in both
    // modes by the adjacent BigInt cases.
    [Theory]
    [InlineData("built-ins/Promise/allKeyed/arg-not-object-reject.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/arg-not-object-reject.js")]
    public void Promise_keyed_combinators_reject_primitive_inputs_with_nested_helper(
        string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    private void AssertPromiseKeyedPass(string relativePath)
    {
        AssertPass(relativePath, Test262ExecutionMode.Interpreted);
        // asyncHelpers.js currently cannot see the compiled runner's top-level
        // $DONE declaration as an own globalThis property. Keep compiled
        // coverage on the synchronous metadata/constructor surface; async
        // behavior is exercised by the compiler's Promise runtime tests.
        string fileName = Path.GetFileName(relativePath);
        if (fileName is "ctx-non-ctor.js" or "extensible.js" or "length.js"
            or "name.js" or "not-a-constructor.js" or "prop-desc.js" or "proto.js")
        {
            AssertPass(relativePath, Test262ExecutionMode.Compiled);
        }
    }

    [Fact]
    public void Math_round_preserves_ecmascript_boundary_cases()
        => AssertPass(
            "built-ins/Math/round/S15.8.2.15_A7.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Math_exposes_standard_toStringTag_metadata()
        => AssertPass(
            "built-ins/Math/Symbol.toStringTag.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Math_sumPrecise_honors_array_iterator_overrides()
        => AssertPass(
            "built-ins/Math/sumPrecise/takes-iterable.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Math_sumPrecise_accumulates_binary64_values_exactly()
        => AssertPass(
            "built-ins/Math/sumPrecise/sum.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Boolean_conversion_treats_objects_as_truthy()
        => AssertPass(
            "built-ins/Boolean/S9.2_A6_T1.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Boolean_prototype_property_is_not_configurable()
        => AssertPass(
            "built-ins/Boolean/prototype/S15.6.3.1_A3.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Boolean/prop-desc.js")]
    [InlineData("built-ins/Math/prop-desc.js")]
    [InlineData("built-ins/Number/prop-desc.js")]
    public void Built_in_global_bindings_have_standard_descriptors(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Number_explicitly_converts_BigInt_values()
        => AssertPass(
            "built-ins/Number/bigint-conversion.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Number_toFixed_uses_standard_notation_threshold()
        => AssertPass(
            "built-ins/Number/prototype/toFixed/S15.7.4.5_A1.4_T01.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Error_isError_recognizes_intrinsic_error_instances()
        => AssertPass(
            "built-ins/Error/isError/errors.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Error/isError/bigints.js")]
    [InlineData("built-ins/Error/isError/error-subclass.js")]
    [InlineData("built-ins/Error/isError/fake-errors.js")]
    [InlineData("built-ins/Error/isError/non-error-objects.js")]
    [InlineData("built-ins/Error/isError/primitives.js")]
    [InlineData("built-ins/Error/isError/symbols.js")]
    public void Error_isError_uses_the_intrinsic_brand(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Error/isError/is-a-constructor.js")]
    [InlineData("built-ins/Error/isError/name.js")]
    [InlineData("built-ins/Error/isError/prop-desc.js")]
    public void Error_isError_has_standard_function_metadata(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Error_and_Function_constructors_report_standard_length()
        => AssertPass(
            "built-ins/Error/length.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Error_omits_message_when_no_message_is_supplied()
        => AssertPass(
            "built-ins/Error/the-initial-value-of-errorprototypemessage-is-the-empty-string.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Error/tostring-1.js")]
    [InlineData("built-ins/Error/tostring-2.js")]
    public void Error_instances_honor_prototype_toString_replacement(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Error_prototype_constructor_builds_branded_instances()
        => AssertPass(
            "built-ins/Error/prototype/constructor/S15.11.4.1_A1_T2.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Error_cause_propagates_abrupt_has_and_get_operations()
        => AssertPass(
            "built-ins/Error/cause_abrupt.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/String/prototype/Symbol.iterator/length.js")]
    [InlineData("built-ins/String/prototype/Symbol.iterator/name.js")]
    [InlineData("built-ins/String/prototype/Symbol.iterator/not-a-constructor.js")]
    [InlineData("built-ins/String/prototype/Symbol.iterator/prop-desc.js")]
    [InlineData("built-ins/String/prototype/Symbol.iterator/this-val-to-str-err.js")]
    public void String_iterator_has_standard_protocol_metadata(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_indexing_rejects_NaN_as_an_array_index()
        => AssertPass(
            "built-ins/String/15.5.5.5.2-3-6.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_trim_handles_line_continuation_whitespace()
        => AssertPass(
            "built-ins/String/prototype/trim/15.5.4.20-4-1.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_call_observes_Array_prototype_toString_override()
        => AssertPass(
            "built-ins/String/S15.5.1.1_A1_T8.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_call_observes_global_toString_override()
        => AssertPass(
            "built-ins/String/S15.5.1.1_A1_T9.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_constructor_falls_back_to_function_valueOf()
        => AssertPass(
            "built-ins/String/S15.5.2.1_A1_T11.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_constructor_observes_Function_prototype_toString_override()
        => AssertPass(
            "built-ins/String/S15.5.2.1_A1_T8.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_index_rejects_NaN_property_key()
        => AssertPass(
            "built-ins/String/15.5.5.5.2-3-6.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_call_coerces_eval_var_result_to_undefined()
        => AssertPass(
            "built-ins/String/S9.8_A1_T1.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_prototype_constructor_constructs_boxed_strings()
        => AssertPass(
            "built-ins/String/prototype/constructor/S15.5.4.1_A1_T2.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_slice_coerces_function_receivers()
        => AssertPass(
            "built-ins/String/prototype/slice/S15.5.4.13_A1_T5.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_localeCompare_treats_canonical_equivalents_as_equal()
        => AssertPass(
            "built-ins/String/prototype/localeCompare/15.5.4.9_CE.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_replace_coerces_RegExp_replacement_objects()
        => AssertPass(
            "built-ins/String/prototype/replace/replaceValue-evaluation-order-regexp-object.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void String_matchAll_rejects_undefined_RegExp_flags()
        => AssertPass(
            "built-ins/String/prototype/matchAll/flags-undefined-throws.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Object_boxes_BigInt_values()
        => AssertPass(
            "built-ins/Object/bigint.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_keys_unbox_Boolean_objects()
        => AssertPass(
            "built-ins/Array/S15.4_A1.1_T6.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_keys_unbox_Number_objects()
        => AssertPass(
            "built-ins/Array/S15.4_A1.1_T7.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_keys_unbox_String_objects()
        => AssertPass(
            "built-ins/Array/S15.4_A1.1_T8.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_keys_use_ordinary_object_ToPrimitive()
        => AssertPass(
            "built-ins/Array/S15.4_A1.1_T9.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_length_truncation_reveals_prototype_indices()
        => AssertPass(
            "built-ins/Array/S15.4.5.1_A1.2_T2.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_constructor_inherits_Function_prototype_expandos()
        => AssertPass(
            "built-ins/Array/S15.4.3_A1.1_T1.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void RegExp_digit_character_class_remains_stable_after_Array_key_changes()
        => AssertPass(
            "built-ins/RegExp/CharacterClassEscapes/character-class-digit-class-escape-positive-cases.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_length_growth_does_not_materialize_prototype_indices()
        => AssertPass(
            "built-ins/Array/length/S15.4.5.1_A1.2_T3.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_from_propagates_iterator_getter_errors()
        => AssertPass(
            "built-ins/Array/from/get-iter-method-err.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Object_assign_reads_symbols_after_strings()
        => AssertPass(
            "built-ins/Object/assign/strings-and-symbol-order.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Object_assign_boxes_Symbol_targets()
        => AssertPass(
            "built-ins/Object/assign/Target-Symbol.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Object_assign_updates_Array_exotic_targets()
        => AssertPass(
            "built-ins/Object/assign/target-Array.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Bound_functions_inherit_Function_prototype_expandos()
        => AssertPassInBothModes(
            "built-ins/Object/defineProperty/15.2.3.6-4-417.js");

    [Fact]
    public void Bound_functions_inherit_Function_prototype_accessors()
        => AssertPass(
            "built-ins/Object/defineProperty/15.2.3.6-4-593.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-118.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-119.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-120.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-121.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-122.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-124.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-167.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-181.js")]
    [InlineData("built-ins/Object/defineProperty/redefine-length-with-various-values-and-configurable-true.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-115.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-116.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-117.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-118.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-120.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-163.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-177.js")]
    [InlineData("built-ins/Array/length/define-own-prop-length-no-value-order.js")]
    public void Array_length_uses_its_intrinsic_descriptor_during_redefinition(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-313-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-313.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-316-1.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-316.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-333-3.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-333-7.js")]
    public void Array_like_named_data_descriptors_round_trip(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-161.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-162.js")]
    public void Date_prototype_methods_retain_data_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Promise_instances_inherit_the_finally_method()
        => AssertPass(
            "built-ins/Promise/prototype/finally/is-a-method.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Promise/prototype/catch/invokes-then.js")]
    [InlineData("built-ins/Promise/prototype/catch/this-value-then-poisoned.js")]
    [InlineData("built-ins/Promise/prototype/catch/this-value-then-throws.js")]
    public void Promise_catch_dynamically_invokes_then(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Promise/prototype/finally/invokes-then-with-function.js")]
    [InlineData("built-ins/Promise/prototype/finally/invokes-then-with-non-function.js")]
    [InlineData("built-ins/Promise/prototype/finally/this-value-then-not-callable.js")]
    [InlineData("built-ins/Promise/prototype/finally/this-value-then-poisoned.js")]
    [InlineData("built-ins/Promise/prototype/finally/this-value-then-throws.js")]
    [InlineData("built-ins/Promise/prototype/finally/this-value-thenable.js")]
    public void Promise_finally_dynamically_invokes_then(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/JSON/rawJSON/basic.js")]
    [InlineData("built-ins/JSON/rawJSON/builtin.js")]
    [InlineData("built-ins/JSON/rawJSON/illegal-empty-and-start-end-chars.js")]
    [InlineData("built-ins/JSON/rawJSON/invalid-JSON-text.js")]
    [InlineData("built-ins/JSON/rawJSON/length.js")]
    [InlineData("built-ins/JSON/rawJSON/name.js")]
    [InlineData("built-ins/JSON/rawJSON/not-a-constructor.js")]
    [InlineData("built-ins/JSON/rawJSON/prop-desc.js")]
    [InlineData("built-ins/JSON/rawJSON/returns-expected-object.js")]
    [InlineData("built-ins/JSON/isRawJSON/basic.js")]
    [InlineData("built-ins/JSON/isRawJSON/builtin.js")]
    [InlineData("built-ins/JSON/isRawJSON/length.js")]
    [InlineData("built-ins/JSON/isRawJSON/name.js")]
    [InlineData("built-ins/JSON/isRawJSON/not-a-constructor.js")]
    [InlineData("built-ins/JSON/isRawJSON/prop-desc.js")]
    public void JSON_raw_values_match_the_spec(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/JSON/parse/text-negative-zero.js")]
    [InlineData("built-ins/JSON/parse/text-non-string-primitive.js")]
    [InlineData("built-ins/JSON/parse/text-object-abrupt.js")]
    [InlineData("built-ins/JSON/parse/text-object.js")]
    public void JSON_parse_coerces_input_with_ToString(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void JSON_parse_keeps_the_last_duplicate_property()
        => AssertPass(
            "built-ins/JSON/parse/duplicate-proto.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void JSON_parse_reviver_visits_own_keys_in_spec_order()
        => AssertPass(
            "built-ins/JSON/parse/reviver-call-order.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void JSON_reviver_preserves_nonconfigurable_array_properties_on_delete()
        => AssertPass(
            "built-ins/JSON/parse/reviver-array-non-configurable-prop-delete.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void JSON_reviver_validates_array_data_property_creation()
        => AssertPass(
            "built-ins/JSON/parse/reviver-array-non-configurable-prop-create.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/JSON/stringify/replacer-array-duplicates.js")]
    [InlineData("built-ins/JSON/stringify/replacer-array-order.js")]
    public void JSON_stringify_preserves_replacer_property_order(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void JSON_stringify_ignores_wrong_type_replacer_entries()
        => AssertPass(
            "built-ins/JSON/stringify/replacer-array-wrong-type.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/JSON/stringify/value-array-circular.js")]
    [InlineData("built-ins/JSON/stringify/value-object-circular.js")]
    [InlineData("built-ins/JSON/stringify/replacer-function-array-circular.js")]
    [InlineData("built-ins/JSON/stringify/replacer-function-object-circular.js")]
    public void JSON_stringify_circular_values_throw_TypeError(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void JSON_stringify_BigInt_throws_TypeError()
        => AssertPass(
            "built-ins/JSON/stringify/value-bigint.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/JSON/stringify/value-object-abrupt.js")]
    [InlineData("built-ins/JSON/stringify/value-tojson-abrupt.js")]
    [InlineData("built-ins/JSON/stringify/value-tojson-arguments.js")]
    [InlineData("built-ins/JSON/stringify/value-tojson-not-function.js")]
    [InlineData("built-ins/JSON/stringify/replacer-function-tojson.js")]
    [InlineData("built-ins/JSON/stringify/value-tojson-object-circular.js")]
    public void JSON_stringify_observes_toJSON_semantics(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/S15.10.4.1_A8_T4.js")]
    [InlineData("built-ins/RegExp/S15.10.4.1_A8_T7.js")]
    [InlineData("built-ins/RegExp/S15.10.4.1_A8_T9.js")]
    [InlineData("built-ins/RegExp/S15.10.4.1_A8_T12.js")]
    public void RegExp_constructor_coerces_pattern_and_flags(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void RegExp_call_only_reuses_matching_constructor_instances()
        => AssertPass(
            "built-ins/RegExp/call_with_regexp_not_same_constructor.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/S15.10.5.1_A1.js")]
    [InlineData("built-ins/RegExp/prototype/S15.10.5.1_A3.js")]
    [InlineData("built-ins/RegExp/prototype/S15.10.5.1_A4.js")]
    public void RegExp_constructor_owns_protected_prototype(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void RegExp_replace_coerces_noncallable_replacement_eagerly()
        => AssertPass(
            "built-ins/RegExp/prototype/Symbol.replace/arg-2-coerce-err.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/flags-tostring-error.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/flags-tostring-error.js")]
    public void RegExp_protocols_propagate_flags_ToString_errors(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/coerce-limit-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/coerce-limit.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/coerce-string-err.js")]
    public void RegExp_split_coerces_string_and_limit(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/coerce-flags.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/limit-0-bail.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/species-ctor-ctor-get-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/species-ctor-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/species-ctor-species-get-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/species-ctor-y.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/species-ctor.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-empty-match-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-get-lastindex-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-match-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-set-lastindex-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-set-lastindex-match.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-set-lastindex-no-match.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/u-lastindex-adv-thru-failure.js")]
    public void RegExp_split_observes_species_exec_and_lastIndex(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/split/arguments-are-new-reg-exp-and-hi-and-instance-is-string-hello.js")]
    [InlineData("built-ins/String/prototype/split/arguments-are-regexp-l-and-hi-and-instance-is-string-hello.js")]
    [InlineData("built-ins/String/prototype/split/argument-is-regexp-and-instance-is-number.js")]
    [InlineData("built-ins/String/prototype/split/call-split-1-boo-instance-is-number.js")]
    [InlineData("built-ins/String/prototype/split/call-split-1-math-pow-2-32-1-instance-is-number.js")]
    [InlineData("built-ins/String/prototype/split/call-split-l-na-n-instance-is-string-hello.js")]
    [InlineData("built-ins/String/prototype/split/cstm-split-get-err.js")]
    [InlineData("built-ins/String/prototype/split/cstm-split-invocation.js")]
    [InlineData("built-ins/String/prototype/split/limit-touint32-error.js")]
    [InlineData("built-ins/String/prototype/split/separator-override-tostring-throws-limit-override-valueof-throws.js")]
    [InlineData("built-ins/String/prototype/split/separator-tostring-error.js")]
    [InlineData("built-ins/String/prototype/split/separator-undef-limit-zero.js")]
    [InlineData("built-ins/String/prototype/split/this-value-tostring-error.js")]
    [InlineData("built-ins/String/prototype/split/this-value-not-obj-coercible.js")]
    [InlineData("built-ins/String/prototype/split/transferred-to-custom.js")]
    public void String_split_observes_symbol_dispatch_and_ToUint32(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/coerce-global.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/exec-return-type-invalid.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/g-match-empty-coerce-lastindex-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/g-match-empty-set-lastindex-err.js")]
    public void RegExp_match_honors_global_exec_and_lastIndex(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.search/cstm-exec-return-invalid.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.search/set-lastindex-init-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.search/set-lastindex-restore-err.js")]
    public void RegExp_search_uses_throwing_lastIndex_writes(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/15.4.5-1.js")]
    [InlineData("built-ins/Array/15.4.5.1-5-1.js")]
    [InlineData("built-ins/Array/15.4.5.1-5-2.js")]
    [InlineData("built-ins/Array/S15.4.1_A1.1_T1.js")]
    [InlineData("built-ins/Array/S15.4.1_A1.1_T2.js")]
    [InlineData("built-ins/Array/S15.4.1_A1.2_T1.js")]
    [InlineData("built-ins/Array/S15.4.1_A1.3_T1.js")]
    [InlineData("built-ins/Array/S15.4.1_A2.1_T1.js")]
    [InlineData("built-ins/Array/S15.4.1_A2.2_T1.js")]
    [InlineData("built-ins/Array/S15.4.1_A3.1_T1.js")]
    [InlineData("built-ins/Array/S15.4.2.1_A1.1_T1.js")]
    [InlineData("built-ins/Array/S15.4.2.1_A1.1_T2.js")]
    [InlineData("built-ins/Array/S15.4.2.1_A1.2_T1.js")]
    [InlineData("built-ins/Array/S15.4.2.1_A1.3_T1.js")]
    [InlineData("built-ins/Array/S15.4.2.1_A2.1_T1.js")]
    [InlineData("built-ins/Array/S15.4.2.1_A2.2_T1.js")]
    [InlineData("built-ins/Array/S15.4.3_A1.1_T2.js")]
    [InlineData("built-ins/Array/S15.4.5.1_A2.1_T1.js")]
    [InlineData("built-ins/Array/S15.4.5.1_A2.2_T1.js")]
    [InlineData("built-ins/Array/S15.4.5.1_A2.3_T1.js")]
    [InlineData("built-ins/Array/S15.4.5.2_A1_T1.js")]
    [InlineData("built-ins/Array/S15.4.5.2_A1_T2.js")]
    [InlineData("built-ins/Array/S15.4.5.2_A2_T1.js")]
    [InlineData("built-ins/Array/S15.4.5.2_A3_T1.js")]
    [InlineData("built-ins/Array/S15.4.5.2_A3_T3.js")]
    [InlineData("built-ins/Array/S15.4_A1.1_T10.js")]
    [InlineData("built-ins/Array/S15.4_A1.1_T4.js")]
    [InlineData("built-ins/Array/S15.4_A1.1_T5.js")]
    public void Array_legacy_exotic_semantics_remain_interpreter_compatible(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/from/Array.from-name.js")]
    [InlineData("built-ins/Array/from/Array.from_arity.js")]
    [InlineData("built-ins/Array/from/array-like-has-length-but-no-indexes-with-values.js")]
    [InlineData("built-ins/Array/from/calling-from-valid-1-noStrict.js")]
    [InlineData("built-ins/Array/from/calling-from-valid-1-onlyStrict.js")]
    [InlineData("built-ins/Array/from/elements-added-after.js")]
    [InlineData("built-ins/Array/from/elements-updated-after.js")]
    [InlineData("built-ins/Array/from/from-array.js")]
    [InlineData("built-ins/Array/from/from-string.js")]
    [InlineData("built-ins/Array/from/items-is-arraybuffer.js")]
    [InlineData("built-ins/Array/from/items-is-null-throws.js")]
    [InlineData("built-ins/Array/from/iter-adv-err.js")]
    [InlineData("built-ins/Array/from/iter-get-iter-err.js")]
    [InlineData("built-ins/Array/from/iter-get-iter-val-err.js")]
    [InlineData("built-ins/Array/from/iter-map-fn-args.js")]
    [InlineData("built-ins/Array/from/iter-map-fn-err.js")]
    [InlineData("built-ins/Array/from/iter-map-fn-return.js")]
    [InlineData("built-ins/Array/from/iter-map-fn-this-non-strict.js")]
    [InlineData("built-ins/Array/from/iter-map-fn-this-strict.js")]
    [InlineData("built-ins/Array/from/iter-set-elem-prop-non-writable.js")]
    [InlineData("built-ins/Array/from/iter-set-elem-prop.js")]
    [InlineData("built-ins/Array/from/iter-set-length.js")]
    [InlineData("built-ins/Array/from/mapfn-is-not-callable-typeerror.js")]
    [InlineData("built-ins/Array/from/mapfn-is-symbol-throws.js")]
    [InlineData("built-ins/Array/from/mapfn-throws-exception.js")]
    [InlineData("built-ins/Array/from/source-object-iterator-1.js")]
    [InlineData("built-ins/Array/from/source-object-iterator-2.js")]
    [InlineData("built-ins/Array/from/source-object-length-set-elem-prop-non-writable.js")]
    [InlineData("built-ins/Array/from/source-object-length.js")]
    [InlineData("built-ins/Array/from/source-object-missing.js")]
    [InlineData("built-ins/Array/from/source-object-without.js")]
    [InlineData("built-ins/Array/from/this-null.js")]
    public void Array_from_preserves_iterable_and_array_like_semantics(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/isArray/15.4.3.2-0-1.js")]
    [InlineData("built-ins/Array/isArray/15.4.3.2-0-2.js")]
    [InlineData("built-ins/Array/isArray/15.4.3.2-0-3.js")]
    [InlineData("built-ins/Array/isArray/15.4.3.2-0-4.js")]
    [InlineData("built-ins/Array/isArray/15.4.3.2-0-6.js")]
    [InlineData("built-ins/Array/isArray/15.4.3.2-0-7.js")]
    [InlineData("built-ins/Array/isArray/15.4.3.2-1-1.js")]
    [InlineData("built-ins/Array/isArray/15.4.3.2-1-10.js")]
    public void Array_isArray_preserves_cross_type_classification(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_array-like-negative-length.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-sparse-object.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_length-throws.js")]
    [InlineData("built-ins/Array/prototype/concat/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/concat/create-non-array.js")]
    [InlineData("built-ins/Array/prototype/concat/is-concat-spreadable-get-err.js")]
    [InlineData("built-ins/Array/prototype/concat/is-concat-spreadable-val-falsey.js")]
    [InlineData("built-ins/Array/prototype/concat/is-concat-spreadable-val-truthy.js")]
    [InlineData("built-ins/Array/prototype/concat/S15.4.4.4_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/concat/S15.4.4.4_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/concat/S15.4.4.4_A3_T3.js")]
    [InlineData("built-ins/Array/prototype/concat/S15.4.4.4_A3_T1.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-string-wrapper.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-number-wrapper.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-function.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-boolean-wrapper.js")]
    [InlineData("built-ins/Array/prototype/concat/S15.4.4.4_A3_T2.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-reg-exp.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-getter-throws.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_sloppy-arguments-throws.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_array-like-primitive-non-number-length.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_array-like-length-value-of-throws.js")]
    [InlineData("built-ins/Array/prototype/concat/Array.prototype.concat_array-like-length-to-string-throws.js")]
    [InlineData("built-ins/Array/prototype/concat/15.4.4.4-5-c-i-1.js")]
    [InlineData("built-ins/Array/prototype/concat/is-concat-spreadable-proxy.js")]
    [InlineData("built-ins/Array/prototype/concat/is-concat-spreadable-is-array-proxy-revoked.js")]
    [InlineData("built-ins/Array/prototype/concat/is-concat-spreadable-proxy-revoked.js")]
    [InlineData("built-ins/Array/prototype/concat/arg-length-exceeding-integer-limit.js")]
    public void Array_concat_honors_generic_and_spreadable_values(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_concat_observes_arguments_index_accessors_in_compiled_mode()
        => AssertPass(
            "built-ins/Array/prototype/concat/Array.prototype.concat_sloppy-arguments-throws.js",
            Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Array/prototype/pop/S15.4.4.6_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/pop/S15.4.4.6_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/pop/S15.4.4.6_A2_T3.js")]
    [InlineData("built-ins/Array/prototype/pop/S15.4.4.6_A2_T4.js")]
    [InlineData("built-ins/Array/prototype/pop/S15.4.4.6_A3_T1.js")]
    [InlineData("built-ins/Array/prototype/pop/S15.4.4.6_A3_T2.js")]
    [InlineData("built-ins/Array/prototype/pop/S15.4.4.6_A3_T3.js")]
    [InlineData("built-ins/Array/prototype/pop/S15.4.4.6_A4_T1.js")]
    [InlineData("built-ins/Array/prototype/pop/S15.4.4.6_A4_T2.js")]
    [InlineData("built-ins/Array/prototype/pop/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/pop/clamps-to-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/pop/length-near-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/pop/set-length-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/pop/set-length-array-length-is-non-writable.js")]
    [InlineData("built-ins/Array/prototype/pop/set-length-zero-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/pop/set-length-zero-array-length-is-non-writable.js")]
    public void Array_pop_mutates_generic_receivers(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A3.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A4_T1.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A4_T2.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A4_T3.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A5_T1.js")]
    [InlineData("built-ins/Array/prototype/push/length-near-integer-limit-set-failure.js")]
    [InlineData("built-ins/Array/prototype/push/length-near-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/push/set-length-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/push/set-length-array-length-is-non-writable.js")]
    [InlineData("built-ins/Array/prototype/push/set-length-zero-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/push/set-length-zero-array-length-is-non-writable.js")]
    public void Array_push_mutates_generic_receivers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A2_T3.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A2_T4.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A2_T5.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A3_T3.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A4_T1.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A4_T2.js")]
    [InlineData("built-ins/Array/prototype/shift/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/shift/set-length-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/shift/set-length-array-length-is-non-writable.js")]
    [InlineData("built-ins/Array/prototype/shift/set-length-zero-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/shift/set-length-zero-array-length-is-non-writable.js")]
    public void Array_shift_mutates_generic_receivers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/unshift/S15.4.4.13_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/unshift/S15.4.4.13_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/unshift/S15.4.4.13_A3_T2.js")]
    [InlineData("built-ins/Array/prototype/unshift/S15.4.4.13_A4_T1.js")]
    [InlineData("built-ins/Array/prototype/unshift/S15.4.4.13_A4_T2.js")]
    [InlineData("built-ins/Array/prototype/unshift/length-near-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/unshift/set-length-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/unshift/set-length-array-length-is-non-writable.js")]
    [InlineData("built-ins/Array/prototype/unshift/set-length-zero-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/unshift/set-length-zero-array-length-is-non-writable.js")]
    public void Array_unshift_mutates_generic_receivers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A4_T1.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A4_T2.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A4_T3.js")]
    [InlineData("built-ins/Array/prototype/push/S15.4.4.7_A5_T1.js")]
    [InlineData("built-ins/Array/prototype/push/length-near-integer-limit-set-failure.js")]
    [InlineData("built-ins/Array/prototype/push/length-near-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/push/clamps-to-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/push/throws-if-integer-limit-exceeded.js")]
    [InlineData("built-ins/Array/prototype/shift/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A2_T3.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A2_T4.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A2_T5.js")]
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A3_T3.js")]
    [InlineData("built-ins/Array/prototype/shift/set-length-zero-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/unshift/S15.4.4.13_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/unshift/S15.4.4.13_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/unshift/length-near-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/unshift/clamps-to-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/unshift/read-only-property.js")]
    [InlineData("built-ins/Array/prototype/unshift/throws-if-integer-limit-exceeded.js")]
    public void Array_mutators_preserve_generic_receivers_in_compiled_mode(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A2_T3.js")]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A3_T3.js")]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A4_T1.js")]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A4_T2.js")]
    [InlineData("built-ins/Array/prototype/reverse/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/reverse/get_if_present_with_delete.js")]
    [InlineData("built-ins/Array/prototype/reverse/length-exceeding-integer-limit-with-object.js")]
    public void Array_reverse_mutates_generic_receivers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/fill/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/fill/coerced-indexes.js")]
    [InlineData("built-ins/Array/prototype/fill/fill-values.js")]
    [InlineData("built-ins/Array/prototype/fill/length-near-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-end-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-end.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-setting-property-value.js")]
    public void Array_fill_mutates_generic_receivers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/copyWithin/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/coerced-values-start-change-start.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/coerced-values-start-change-target.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/length-near-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-delete-proxy-target.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-delete-target.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-end-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-end.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-set-target-value.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-start-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-start.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-target-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-target.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-this-length-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-this-length.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-this.js")]
    public void Array_copyWithin_mutates_generic_receivers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A2_T1.js")]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A2_T2.js")]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A2_T3.js")]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A3_T3.js")]
    [InlineData("built-ins/Array/prototype/reverse/S15.4.4.8_A4_T2.js")]
    [InlineData("built-ins/Array/prototype/reverse/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/reverse/length-exceeding-integer-limit-with-object.js")]
    [InlineData("built-ins/Array/prototype/fill/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/fill/length-near-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-setting-property-value.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/call-with-boolean.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/length-near-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-delete-proxy-target.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-delete-target.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-set-target-value.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-this.js")]
    public void Generic_reverse_fill_and_copyWithin_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/fill/coerced-indexes.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-end-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-end.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-start-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-start.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-end-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-end.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-start-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-start.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-target-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-target.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/coerced-values-end.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/coerced-values-start-change-start.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/coerced-values-start-change-target.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/fill-holes.js")]
    public void Native_fill_and_copyWithin_coerce_indexes_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/sort/precise-getter-appends-elements.js")]
    [InlineData("built-ins/Array/prototype/sort/precise-setter-deletes-successor.js")]
    public void Array_sort_observes_index_descriptors_in_compiled_mode(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Promise/resolve-non-obj-immed.js")]
    [InlineData("built-ins/Promise/resolve-function-prototype.js")]
    [InlineData("built-ins/Promise/executor-function-prototype.js")]
    [InlineData("built-ins/Promise/resolve/ctx-ctor.js")]
    [InlineData("built-ins/Promise/all/ctx-ctor.js")]
    public void Promise_capabilities_follow_builtin_and_constructor_contracts(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-2-12.js")]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-2-13.js")]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-2-14.js")]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-2-15.js")]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-2-16.js")]
    [InlineData("built-ins/Object/getPrototypeOf/15.2.3.2-2-17.js")]
    public void Native_error_constructor_prototype_chain_matches_error(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Error/cause_property.js")]
    [InlineData("built-ins/Error/message_property.js")]
    public void Error_instances_expose_spec_own_descriptors(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/charCodeAt/S15.5.4.5_A1_T5.js")]
    [InlineData("built-ins/String/prototype/charCodeAt/S15.5.4.5_A1_T6.js")]
    [InlineData("built-ins/String/prototype/charCodeAt/S15.5.4.5_A1_T7.js")]
    [InlineData("built-ins/String/prototype/charCodeAt/S15.5.4.5_A1_T8.js")]
    [InlineData("built-ins/String/prototype/charCodeAt/S15.5.4.5_A1_T9.js")]
    [InlineData("built-ins/String/prototype/charCodeAt/S15.5.4.5_A1_T10.js")]
    [InlineData("built-ins/String/prototype/charCodeAt/pos-coerce-string.js")]
    [InlineData("built-ins/String/prototype/at/index-argument-tointeger.js")]
    [InlineData("built-ins/String/prototype/at/index-non-numeric-argument-tointeger-invalid.js")]
    [InlineData("built-ins/String/prototype/at/index-non-numeric-argument-tointeger.js")]
    public void String_position_arguments_use_ecmascript_number_coercion(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/concat/S15.5.4.6_A1_T10.js")]
    [InlineData("built-ins/String/prototype/concat/S15.5.4.6_A1_T6.js")]
    [InlineData("built-ins/String/prototype/concat/S15.5.4.6_A4_T1.js")]
    public void String_concat_applies_observable_to_string_coercion(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Array/prototype/push/throws-with-string-receiver.js")]
    [InlineData("built-ins/Array/prototype/pop/throws-with-string-receiver.js")]
    [InlineData("built-ins/Array/prototype/unshift/throws-with-string-receiver.js")]
    [InlineData("built-ins/Array/prototype/fill/fill-values.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-this-length.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-this-length.js")]
    public void Array_mutators_called_generically_update_the_original_receiver(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/isWellFormed/return-abrupt-from-this.js")]
    [InlineData("built-ins/String/prototype/isWellFormed/returns-boolean.js")]
    [InlineData("built-ins/String/prototype/isWellFormed/to-string.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/return-abrupt-from-this.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/returns-well-formed-string.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/to-string.js")]
    public void String_well_formed_methods_scan_utf16_code_units(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/S15.5.5.1_A2.js")]
    [InlineData("built-ins/String/S15.5.5.1_A3.js")]
    [InlineData("built-ins/String/S15.5.5.1_A4_T2.js")]
    [InlineData("built-ins/String/length.js")]
    [InlineData("built-ins/String/numeric-properties.js")]
    public void Boxed_strings_expose_exotic_length_and_index_descriptors(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Fact]
    public void String_exotic_indices_reject_non_integral_numeric_keys()
        => AssertPass("built-ins/String/15.5.5.5.2-3-6.js", Test262ExecutionMode.Compiled);

    [Fact]
    public void String_call_form_uses_symbol_descriptive_string()
        => AssertPass("built-ins/String/symbol-string-coercion.js", Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/S15.5.1.1_A1_T6.js")]
    public void String_construction_observes_primitive_conversion(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-get-err.js")]
    [InlineData("built-ins/String/prototype/match/cstm-matcher-invocation.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-get-matchAll-throws.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-matchAll-invocation.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-matchAll-throws.js")]
    [InlineData("built-ins/String/prototype/search/cstm-search-get-err.js")]
    [InlineData("built-ins/String/prototype/search/cstm-search-invocation.js")]
    public void String_symbol_protocol_methods_dispatch_object_overrides(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/matchAll/cstm-matchall-on-boolean-primitive.js")]
    [InlineData("built-ins/String/prototype/matchAll/cstm-matchall-on-number-primitive.js")]
    [InlineData("built-ins/String/prototype/matchAll/cstm-matchall-on-string-primitive.js")]
    [InlineData("built-ins/String/prototype/matchAll/flags-undefined-throws.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-is-null.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-is-undefined.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-is-undefined-or-null-invokes-matchAll.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-matchAll-is-undefined-or-null.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-prototype-get-matchAll-throws.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-prototype-has-no-matchAll.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-prototype-matchAll-invocation.js")]
    [InlineData("built-ins/String/prototype/matchAll/regexp-prototype-matchAll-throws.js")]
    [InlineData("built-ins/String/prototype/matchAll/toString-this-val.js")]
    public void String_matchAll_follows_the_ES2026_symbol_protocol(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/builtin-success-g-set-lastindex.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/coerce-global.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/get-global-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.match/get-unicode-error.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/get-unicode-error.js")]
    public void RegExp_intrinsic_accessors_keep_the_internal_slot_fast_path(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/replace/cstm-replace-get-err.js")]
    [InlineData("built-ins/String/prototype/replace/cstm-replace-invocation.js")]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-call-abrupt.js")]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-call-matching-empty.js")]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-call-tostring-abrupt.js")]
    [InlineData("built-ins/String/prototype/replaceAll/replaceValue-value-tostring.js")]
    public void String_replace_protocol_preserves_custom_dispatch_and_replacement_values(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/normalize/return-abrupt-from-form-as-symbol.js")]
    [InlineData("built-ins/String/prototype/normalize/return-abrupt-from-form.js")]
    [InlineData("built-ins/String/prototype/normalize/return-normalized-string-from-coerced-form.js")]
    [InlineData("built-ins/String/prototype/normalize/return-normalized-string-using-default-parameter.js")]
    public void String_normalize_coerces_the_form_after_applying_the_default(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/Symbol.iterator/length.js")]
    [InlineData("built-ins/String/prototype/Symbol.iterator/name.js")]
    [InlineData("built-ins/String/prototype/Symbol.iterator/not-a-constructor.js")]
    [InlineData("built-ins/String/prototype/Symbol.iterator/prop-desc.js")]
    [InlineData("built-ins/String/prototype/Symbol.iterator/this-val-to-str-err.js")]
    public void String_iterator_is_a_symbol_keyed_unicode_iterator(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-19.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-21.js")]
    [InlineData("built-ins/Object/defineProperty/8.12.9-9-c-i_1.js")]
    [InlineData("built-ins/Object/defineProperty/8.12.9-9-c-i_2.js")]
    public void Object_descriptors_preserve_omitted_fields_during_redefinition(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-2-17.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-8-b-3.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-9-1.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-9-10.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-9-b-12.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-9-b-15.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-9-b-16.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-9-b-25.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-9-b-28.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-9-b-29.js")]
    [InlineData("built-ins/Array/prototype/reduce/15.4.4.21-9-c-ii-4-s.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-8-b-3.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-9-b-16.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-9-b-25.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-9-b-29.js")]
    [InlineData("built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-ii-4-s.js")]
    public void Array_reduce_snapshots_length_and_observes_dynamic_properties(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/find/predicate-is-not-callable-throws.js")]
    [InlineData("built-ins/Array/prototype/findIndex/predicate-is-not-callable-throws.js")]
    [InlineData("built-ins/Array/prototype/findLast/predicate-is-not-callable-throws.js")]
    [InlineData("built-ins/Array/prototype/findLastIndex/predicate-is-not-callable-throws.js")]
    [InlineData("built-ins/Array/prototype/flatMap/non-callable-argument-throws.js")]
    public void Array_predicate_methods_reject_non_callable_callbacks(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/sort/comparefn-nonfunction-call-throws.js")]
    public void Array_sort_rejects_non_callable_comparators(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/executor-call-context-strict.js")]
    public void Promise_jobs_invoke_guest_functions_with_undefined_this(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/String/prototype/indexOf/position-tointeger-bigint.js")]
    [InlineData("built-ins/String/prototype/indexOf/position-tointeger-wrapped-values.js")]
    [InlineData("built-ins/String/prototype/includes/return-false-with-out-of-bounds-position.js")]
    [InlineData("built-ins/String/prototype/codePointAt/return-code-unit-coerced-position.js")]
    public void String_positions_use_ToIntegerOrInfinity(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/invoke-resolve-return.js")]
    [InlineData("built-ins/Promise/all/new-resolve-function.js")]
    [InlineData("built-ins/Promise/all/resolve-element-function-prototype.js")]
    [InlineData("built-ins/Promise/all/same-reject-function.js")]
    [InlineData("built-ins/Promise/allSettled/invoke-resolve-return.js")]
    [InlineData("built-ins/Promise/allSettled/new-reject-function.js")]
    [InlineData("built-ins/Promise/allSettled/new-resolve-function.js")]
    [InlineData("built-ins/Promise/allSettled/reject-element-function-prototype.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-element-function-prototype.js")]
    [InlineData("built-ins/Promise/any/invoke-resolve-return.js")]
    [InlineData("built-ins/Promise/any/new-reject-function.js")]
    [InlineData("built-ins/Promise/any/reject-element-function-prototype.js")]
    [InlineData("built-ins/Promise/race/invoke-resolve-return.js")]
    [InlineData("built-ins/Promise/race/same-reject-function.js")]
    [InlineData("built-ins/Promise/race/same-resolve-function.js")]
    public void Promise_combinators_adopt_resolved_thenables(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/resolve-not-callable-reject-with-typeerror.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-not-callable-reject-with-typeerror.js")]
    [InlineData("built-ins/Promise/any/resolve-not-callable-reject-with-typeerror.js")]
    public void Promise_combinators_reject_non_callable_resolve(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/push/set-length-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/push/set-length-array-length-is-non-writable.js")]
    [InlineData("built-ins/Array/prototype/push/set-length-zero-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/push/set-length-zero-array-length-is-non-writable.js")]
    [InlineData("built-ins/Array/prototype/shift/set-length-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/shift/set-length-array-length-is-non-writable.js")]
    [InlineData("built-ins/Array/prototype/shift/set-length-zero-array-length-is-non-writable.js")]
    [InlineData("built-ins/Array/prototype/unshift/set-length-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/unshift/set-length-array-length-is-non-writable.js")]
    [InlineData("built-ins/Array/prototype/unshift/set-length-zero-array-is-frozen.js")]
    [InlineData("built-ins/Array/prototype/unshift/set-length-zero-array-length-is-non-writable.js")]
    public void Array_mutators_observe_index_accessors_and_length_integrity(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-178.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-179.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-5.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-6.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-7.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-8.js")]
    public void Global_builtin_descriptors_match_standard_attributes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/indexOf/15.4.4.14-9-a-19.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-19.js")]
    public void Array_search_preserves_nonconfigurable_tail_elements(string relativePath)
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
