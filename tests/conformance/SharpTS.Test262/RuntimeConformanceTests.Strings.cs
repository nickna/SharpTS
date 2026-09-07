using Xunit;

namespace SharpTS.Test262;

public sealed partial class RuntimeConformanceTests
{

    [Fact]
    public void RegExp_prototype_function_metadata_is_isolated_between_realms()
    {
        const string relativePath =
            "built-ins/RegExp/prototype/test/S15.10.6.3_A9.js";
        AssertPass(relativePath, Test262ExecutionMode.Interpreted);
        AssertPass(relativePath, Test262ExecutionMode.Interpreted);
    }

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
    [InlineData("built-ins/RegExp/escape/prop-desc.js")]
    [InlineData("built-ins/RegExp/lastIndex.js")]
    [InlineData("built-ins/RegExp/prototype/exec/S15.10.6.2_A1_T17.js")]
    [InlineData("built-ins/RegExp/prototype/S15.10.6.1_A1_T2.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/species-constructor-is-undefined.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/species-constructor-species-is-null-or-undefined.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/string-tostring.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/this-tolength-lastindex-throws.js")]
    [InlineData("built-ins/RegExp/prototype/test/y-fail-lastindex-no-write.js")]
    [InlineData("built-ins/RegExp/S15.10.4.1_A4_T1.js")]
    [InlineData("built-ins/RegExp/S15.10.4.1_A4_T4.js")]
    [InlineData("built-ins/RegExp/S15.10.4.1_A5_T6.js")]
    public void RegExp_remaining_track_b_cases_pass_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/isregexp-this-throws.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/regexpcreate-this-throws.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/species-constructor-get-constructor-throws.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/species-constructor-get-species-throws.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/species-constructor-is-not-object-throws.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/species-constructor-species-is-not-constructor.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/species-constructor-species-throws.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/this-get-flags.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/this-tostring-flags.js")]
    public void RegExp_matchAll_observes_species_and_dynamic_properties_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/species-constructor.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/this-lastindex-cached.js")]
    public void RegExp_matchAll_additional_cases_pass_in_interpreted_mode(
        string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/coerce-flags-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/get-flags-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/species-ctor-species-non-ctor.js")]
    public void RegExp_split_preserves_abrupt_flags_and_species_checks_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/RegExp/prototype/Symbol.replace/coerce-lastindex-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-coerce-lastindex-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-coerce-lastindex.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-result-coerce-length-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-result-coerce-length.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-result-get-capture-err.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.split/str-result-get-length-err.js")]
    public void RegExp_protocols_coerce_array_like_match_state_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void RegExp_global_descriptor_matches_in_both_modes()
        => AssertPassInBothModes("built-ins/RegExp/prop-desc.js");

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
    [InlineData("built-ins/String/prototype/isWellFormed/to-string-primitive.js")]
    [InlineData("built-ins/String/prototype/toWellFormed/to-string-primitive.js")]
    public void String_well_formed_methods_ignore_primitive_prototype_overrides(string relativePath)
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
    [InlineData("built-ins/String/S8.12.8_A1.js")]
    [InlineData("built-ins/String/S9.8_A5_T1.js")]
    public void String_legacy_coercion_matches_compiled_mode(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Bound_String_match_retains_its_receiver()
        => AssertPassInBothModes(
            "built-ins/String/prototype/match/S15.5.4.10_A1_T3.js");

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
    public void RegExp_digit_character_class_remains_stable_after_Array_key_changes()
        => AssertPass(
            "built-ins/RegExp/CharacterClassEscapes/character-class-digit-class-escape-positive-cases.js",
            Test262ExecutionMode.Interpreted);

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
    [InlineData("built-ins/RegExp/prototype/Symbol.match/u-advance-after-empty.js")]
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
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/species-regexp-get-global-throws.js")]
    [InlineData("built-ins/RegExp/prototype/Symbol.matchAll/species-regexp-get-unicode-throws.js")]
    public void Compiled_matchAll_uses_original_flags_after_species_construction(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("built-ins/String/prototype/indexOf/position-tointeger-bigint.js")]
    [InlineData("built-ins/String/prototype/indexOf/position-tointeger-wrapped-values.js")]
    [InlineData("built-ins/String/prototype/includes/return-false-with-out-of-bounds-position.js")]
    [InlineData("built-ins/String/prototype/codePointAt/return-code-unit-coerced-position.js")]
    public void String_positions_use_ToIntegerOrInfinity(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> Issue1279CompiledRegExpTimeoutCases => new()
    {
        "built-ins/RegExp/CharacterClassEscapes/character-class-digit-class-escape-negative-cases.js",
        "built-ins/RegExp/CharacterClassEscapes/character-class-non-digit-class-escape-positive-cases.js",
        "built-ins/RegExp/CharacterClassEscapes/character-class-non-whitespace-class-escape-positive-cases.js",
        "built-ins/RegExp/CharacterClassEscapes/character-class-non-word-class-escape-positive-cases.js",
        "built-ins/RegExp/CharacterClassEscapes/character-class-whitespace-class-escape-negative-cases.js",
        "built-ins/RegExp/CharacterClassEscapes/character-class-word-class-escape-negative-cases.js",
    };

    [Theory]
    [MemberData(nameof(Issue1279CompiledRegExpTimeoutCases))]
    public void Issue1279_compiled_RegExp_timeout_guards_pass(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [MemberData(nameof(Issue1279CompiledRegExpTimeoutCases))]
    public void Issue1279_interpreted_RegExp_timeout_guards_pass(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted, TimeSpan.FromSeconds(60));

    public static TheoryData<string> RemainingStringCompilerParityCases => new()
    {
        "built-ins/String/prototype/constructor/S15.5.4.1_A1_T2.js",
        "built-ins/String/prototype/indexOf/position-tointeger-toprimitive.js",
        "built-ins/String/prototype/indexOf/searchstring-tostring-bigint.js",
        "built-ins/String/prototype/isWellFormed/to-string-primitive.js",
        "built-ins/String/prototype/lastIndexOf/S15.5.4.8_A4_T1.js",
        "built-ins/String/prototype/lastIndexOf/S15.5.4.8_A4_T2.js",
        "built-ins/String/prototype/localeCompare/15.5.4.9_CE.js",
        "built-ins/String/prototype/match/cstm-matcher-is-null.js",
        "built-ins/String/prototype/match/invoke-builtin-match.js",
        "built-ins/String/prototype/replace/regexp-capture-by-index.js",
        "built-ins/String/prototype/replace/replaceValue-evaluation-order-regexp-object.js",
        "built-ins/String/prototype/replaceAll/replaceValue-call-each-match-position.js",
        "built-ins/String/prototype/replaceAll/replaceValue-fn-skip-toString.js",
        "built-ins/String/prototype/replaceAll/searchValue-replacer-call.js",
        "built-ins/String/prototype/replaceAll/searchValue-replacer-is-null.js",
        "built-ins/String/prototype/search/cstm-search-is-null.js",
        "built-ins/String/prototype/search/invoke-builtin-search-searcher-undef.js",
        "built-ins/String/prototype/search/invoke-builtin-search.js",
        "built-ins/String/prototype/slice/S15.5.4.13_A1_T5.js",
        "built-ins/String/prototype/toLocaleLowerCase/name.js",
        "built-ins/String/prototype/toLocaleUpperCase/name.js",
        "built-ins/String/prototype/toWellFormed/to-string-primitive.js",
        "built-ins/String/prototype/valueOf/name.js",
        "built-ins/String/prototype/valueOf/non-generic.js",
        "built-ins/String/raw/raw.js",
        "built-ins/String/S15.5.1.1_A1_T9.js",
        "built-ins/String/S15.5.2.1_A1_T11.js",
    };

    [Theory]
    [MemberData(nameof(RemainingStringCompilerParityCases))]
    public void Remaining_String_compiler_parity(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    public static TheoryData<string> RemainingRegExpCompilerParityCases => new()
    {
        "built-ins/RegExp/prototype/Symbol.matchAll/species-constructor.js",
        "built-ins/RegExp/prototype/Symbol.matchAll/this-lastindex-cached.js",
    };

    [Theory]
    [MemberData(nameof(RemainingRegExpCompilerParityCases))]
    public void Remaining_RegExp_compiler_parity(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);
}
