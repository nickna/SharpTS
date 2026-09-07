using Xunit;

namespace SharpTS.Test262;

public sealed partial class RuntimeConformanceTests
{

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

    /// <summary>Annex B §B.2.2.2–5 accessor helpers on <c>Object.prototype</c>.</summary>
    [Theory]
    [InlineData("built-ins/Object/prototype/__defineGetter__/length.js")]
    [InlineData("built-ins/Object/prototype/__defineSetter__/length.js")]
    [InlineData("built-ins/Object/prototype/__lookupGetter__/length.js")]
    [InlineData("built-ins/Object/prototype/__lookupSetter__/length.js")]
    public void Annex_B_accessor_helpers_exist_in_both_modes(string relativePath)
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

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-623.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-624.js")]
    public void Date_prototype_methods_expose_standard_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/prototype/__lookupGetter__/lookup-own-acsr-w-getter.js")]
    [InlineData("built-ins/Object/prototype/__lookupGetter__/lookup-proto-acsr-w-getter.js")]
    [InlineData("built-ins/Object/prototype/__lookupGetter__/lookup-own-proto-err.js")]
    [InlineData("built-ins/Object/prototype/__lookupGetter__/lookup-proto-proto-err.js")]
    [InlineData("built-ins/Object/prototype/__lookupGetter__/this-non-obj.js")]
    [InlineData("built-ins/Object/prototype/__lookupSetter__/lookup-own-acsr-w-setter.js")]
    [InlineData("built-ins/Object/prototype/__lookupSetter__/lookup-proto-acsr-w-setter.js")]
    [InlineData("built-ins/Object/prototype/__lookupSetter__/lookup-own-proto-err.js")]
    [InlineData("built-ins/Object/prototype/__lookupSetter__/lookup-proto-proto-err.js")]
    [InlineData("built-ins/Object/prototype/__lookupSetter__/this-non-obj.js")]
    public void Object_legacy_accessor_lookup_walks_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Object_isPrototypeOf_observes_proxy_getPrototypeOf()
        => AssertPassInBothModes(
            "built-ins/Object/prototype/isPrototypeOf/arg-is-proxy.js");

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
    [InlineData("built-ins/Object/setPrototypeOf/o-not-obj.js")]
    [InlineData("built-ins/Object/setPrototypeOf/proto-not-obj.js")]
    [InlineData("built-ins/Object/setPrototypeOf/set-error.js")]
    [InlineData("built-ins/Object/setPrototypeOf/set-failure-cycle.js")]
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

    [Theory]
    [InlineData("built-ins/Object/prototype/setPrototypeOf-with-different-values.js")]
    [InlineData("built-ins/Object/prototype/setPrototypeOf-with-non-circular-values.js")]
    [InlineData("built-ins/Object/prototype/setPrototypeOf-with-non-circular-values-__proto__.js")]
    [InlineData("built-ins/Object/prototype/setPrototypeOf-with-same-value.js")]
    public void Object_prototype_has_an_immutable_null_prototype(string relativePath)
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

    [Fact]
    public void Object_boxes_BigInt_values()
        => AssertPass(
            "built-ins/Object/bigint.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Object/assign/source-get-attr-error.js")]
    [InlineData("built-ins/Object/assign/source-own-prop-desc-missing.js")]
    [InlineData("built-ins/Object/assign/source-own-prop-error.js")]
    [InlineData("built-ins/Object/assign/source-own-prop-keys-error.js")]
    [InlineData("built-ins/Object/assign/strings-and-symbol-order.js")]
    [InlineData("built-ins/Object/assign/target-Array.js")]
    [InlineData("built-ins/Object/assign/target-is-non-extensible-property-creation-throws.js")]
    [InlineData("built-ins/Object/assign/target-is-sealed-property-creation-throws.js")]
    [InlineData("built-ins/Object/assign/Target-Symbol.js")]
    public void Object_assign_uses_shared_own_key_get_and_strict_set_semantics(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptors/proxy-no-ownkeys-returned-keys-order.js")]
    [InlineData("built-ins/Object/getOwnPropertyNames/proxy-invariant-absent-not-configurable-symbol-key.js")]
    [InlineData("built-ins/Object/getOwnPropertyNames/proxy-invariant-duplicate-symbol-entry.js")]
    [InlineData("built-ins/Object/getOwnPropertyNames/proxy-invariant-not-extensible-absent-symbol-key.js")]
    [InlineData("built-ins/Object/getOwnPropertyNames/proxy-invariant-not-extensible-extra-symbol-key.js")]
    [InlineData("built-ins/Object/getOwnPropertySymbols/proxy-invariant-absent-not-configurable-string-key.js")]
    [InlineData("built-ins/Object/getOwnPropertySymbols/proxy-invariant-duplicate-string-entry.js")]
    [InlineData("built-ins/Object/getOwnPropertySymbols/proxy-invariant-not-extensible-absent-string-key.js")]
    [InlineData("built-ins/Object/getOwnPropertySymbols/proxy-invariant-not-extensible-extra-string-key.js")]
    [InlineData("built-ins/Object/keys/property-traps-order-with-proxied-array.js")]
    [InlineData("built-ins/Object/keys/proxy-keys.js")]
    [InlineData("built-ins/Object/keys/proxy-non-enumerable-prop-invariant-1.js")]
    [InlineData("built-ins/Object/keys/proxy-non-enumerable-prop-invariant-2.js")]
    [InlineData("built-ins/Object/keys/proxy-non-enumerable-prop-invariant-3.js")]
    public void Object_own_key_consumers_share_proxy_validation_and_filtering(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptors/symbols-included.js")]
    [InlineData("built-ins/Object/freeze/frozen-object-contains-symbol-properties-non-strict.js")]
    [InlineData("built-ins/Object/freeze/frozen-object-contains-symbol-properties-strict.js")]
    public void Symbol_properties_use_ordinary_descriptor_and_integrity_semantics(
        string relativePath)
        => AssertPassInBothModes(relativePath);

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
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-19.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-21.js")]
    [InlineData("built-ins/Object/defineProperty/8.12.9-9-c-i_1.js")]
    [InlineData("built-ins/Object/defineProperty/8.12.9-9-c-i_2.js")]
    public void Object_descriptors_preserve_omitted_fields_during_redefinition(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Object/create/15.2.3.5-4-30.js")]
    [InlineData("built-ins/Object/create/15.2.3.5-4-5.js")]
    [InlineData("built-ins/Object/create/15.2.3.5-4-7.js")]
    [InlineData("built-ins/Object/create/properties-arg-to-object-non-empty-string.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-15.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-8.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-2-9.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-16.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-a-9.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-b-241.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-5-b-248.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-12.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-17.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-18.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-19.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-198.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-2.js")]
    [InlineData("built-ins/Object/defineProperties/15.2.3.7-6-a-21.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-411.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-587.js")]
    [InlineData("built-ins/Object/keys/15.2.3.14-5-13.js")]
    public void Compiled_object_descriptor_carriers_remaining_parity(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Object/entries/observable-operations.js")]
    [InlineData("built-ins/Object/entries/order-after-define-property-with-function.js")]
    [InlineData("built-ins/Object/entries/return-order.js")]
    [InlineData("built-ins/Object/entries/tamper-with-object-keys.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptors/observable-operations.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptors/order-after-define-property.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptors/tamper-with-object-keys.js")]
    [InlineData("built-ins/Object/getOwnPropertyNames/order-after-define-property.js")]
    [InlineData("built-ins/Object/keys/order-after-define-property-with-function.js")]
    [InlineData("built-ins/Object/keys/order-after-define-property.js")]
    [InlineData("built-ins/Object/keys/return-order.js")]
    [InlineData("built-ins/Object/values/observable-operations.js")]
    [InlineData("built-ins/Object/values/order-after-define-property.js")]
    [InlineData("built-ins/Object/values/return-order.js")]
    [InlineData("built-ins/Object/values/tamper-with-object-keys.js")]
    public void Compiled_object_enumeration_remaining_parity(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/Object/groupBy/groupLength.js")]
    [InlineData("built-ins/Object/groupBy/invalid-iterable.js")]
    [InlineData("built-ins/Object/groupBy/null-prototype.js")]
    public void Compiled_object_group_by_remaining_parity(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

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
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-115.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-30.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-31.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-45.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-622.js")]
    public void Intrinsic_descriptor_carriers_share_identity_and_mutation_state(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/preventExtensions/15.2.3.10-3-11.js")]
    [InlineData("built-ins/Object/preventExtensions/15.2.3.10-3-14.js")]
    [InlineData("built-ins/Object/preventExtensions/15.2.3.10-3-21.js")]
    [InlineData("built-ins/Object/preventExtensions/abrupt-completion.js")]
    [InlineData("built-ins/Object/preventExtensions/symbol-object-contains-symbol-properties-strict.js")]
    [InlineData("built-ins/Object/preventExtensions/throws-when-false.js")]
    public void PreventExtensions_is_shared_by_property_carriers_and_proxies(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-408.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-581.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-593.js")]
    public void Intrinsic_carriers_follow_inherited_property_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/bigint.js")]
    [InlineData("built-ins/Object/prototype/constructor/S15.2.4.1_A1_T2.js")]
    [InlineData("built-ins/Object/prototype/toString/Object.prototype.toString.call-function.js")]
    public void Object_conversion_preserves_guest_boxing_and_callable_branding(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-292-2.js")]
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-293-4.js")]
    public void Strict_arguments_indices_use_ordinary_descriptor_state(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> AccessorBackedDescriptorFieldCases => new()
    {
        "built-ins/Object/create/15.2.3.5-4-52.js",
        "built-ins/Object/create/15.2.3.5-4-105.js",
        "built-ins/Object/create/15.2.3.5-4-158.js",
        "built-ins/Object/create/15.2.3.5-4-184.js",
        "built-ins/Object/create/15.2.3.5-4-237.js",
        "built-ins/Object/create/15.2.3.5-4-272.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-26.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-79.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-132.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-158.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-211.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-241.js",
        "built-ins/Object/defineProperties/15.2.3.7-5-b-12.js",
        "built-ins/Object/defineProperties/15.2.3.7-5-b-65.js",
        "built-ins/Object/defineProperties/15.2.3.7-5-b-118.js",
        "built-ins/Object/defineProperties/15.2.3.7-5-b-197.js",
        "built-ins/Object/defineProperties/15.2.3.7-5-b-232.js",
    };

    [Theory]
    [MemberData(nameof(AccessorBackedDescriptorFieldCases))]
    public void Property_descriptor_fields_invoke_own_accessors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-a-5.js")]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-a-6.js")]
    [InlineData("built-ins/Object/freeze/15.2.3.9-2-a-9.js")]
    [InlineData("built-ins/Object/seal/object-seal-p-is-own-accessor-property-that-overrides-an-inherited-accessor-property.js")]
    [InlineData("built-ins/Object/seal/object-seal-p-is-own-accessor-property-that-overrides-an-inherited-data-property.js")]
    public void Integrity_levels_preserve_own_accessor_and_function_properties(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Object/getOwnPropertyDescriptors/exception-not-object-coercible.js")]
    [InlineData("built-ins/Object/prototype/toString/symbol-tag-non-str-bigint.js")]
    [InlineData("built-ins/Object/S15.2.3_A3.js")]
    public void Remaining_object_builtin_surface_matches_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> Issue1374ReproducedInterpreterRegressionCases => new()
    {
        "built-ins/Object/defineProperties/15.2.3.7-6-a-202.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-280.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-286.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-287.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-360-1.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-360-2.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-360-5.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-360-6.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-531-15.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-531-16.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-540-4.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-540-5.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-540-9.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-540-10.js",
    };

    [Theory]
    [MemberData(nameof(Issue1374ReproducedInterpreterRegressionCases))]
    public void Issue_1374_reproduced_accessor_descriptor_regressions_match_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);
}
