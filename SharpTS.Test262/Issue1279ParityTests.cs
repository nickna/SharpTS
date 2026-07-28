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
