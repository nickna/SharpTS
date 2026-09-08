using Xunit;

namespace SharpTS.Test262;

public sealed partial class RuntimeConformanceTests
{

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

    /// <summary>ECMA-262 §25.5.1: malformed JSON text is a guest <c>SyntaxError</c> object.</summary>
    [Theory]
    [InlineData("built-ins/JSON/parse/15.12.1.1-0-1.js")]
    [InlineData("built-ins/JSON/parse/15.12.1.1-0-2.js")]
    [InlineData("built-ins/JSON/parse/15.12.1.1-0-3.js")]
    public void JSON_parse_throws_a_SyntaxError_object(string relativePath)
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
    [InlineData("built-ins/Error/prototype/toString/15.11.4.4-8-1.js")]
    public void Error_toString_uses_strict_generic_object_semantics(string relativePath)
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

    [Fact]
    public void JSON_stringify_calls_bigint_toJSON_before_replacer()
        => AssertPass(
            "built-ins/JSON/stringify/value-bigint-order.js",
            Test262ExecutionMode.Interpreted);

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
    [InlineData("built-ins/Symbol/prototype/constructor.js")]
    public void Symbol_prototype_constructor_has_ordinary_descriptor(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

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
    [InlineData("built-ins/BigInt/prototype/toString/radix-err.js")]
    [InlineData("built-ins/BigInt/prototype/toString/radix-tointegerorinfinity-throws-symbol.js")]
    [InlineData("built-ins/BigInt/prototype/toString/radix-tointegerorinfinity-throws-toprimitive-or-bigint.js")]
    public void BigInt_toString_coerces_and_validates_radix(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

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

    [Fact]
    public void Math_round_preserves_ecmascript_boundary_cases()
        => AssertPassInBothModes("built-ins/Math/round/S15.8.2.15_A7.js");

    [Fact]
    public void Math_exposes_standard_toStringTag_metadata()
        => AssertPassInBothModes("built-ins/Math/Symbol.toStringTag.js");

    [Fact]
    public void Compiled_JSON_exposes_standard_toStringTag_metadata()
        => AssertPass(
            "built-ins/JSON/Symbol.toStringTag.js",
            Test262ExecutionMode.Compiled);

    [Fact]
    public void Math_sumPrecise_honors_array_iterator_overrides()
        => AssertPassInBothModes("built-ins/Math/sumPrecise/takes-iterable.js");

    [Fact]
    public void Math_sumPrecise_accumulates_binary64_values_exactly()
        => AssertPassInBothModes("built-ins/Math/sumPrecise/sum.js");

    [Fact]
    public void Math_sumPrecise_rejects_non_number_elements_without_coercion()
        => AssertPassInBothModes("built-ins/Math/sumPrecise/throws-on-non-number.js");

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

    [Fact]
    public void Number_explicitly_converts_BigInt_values()
        => AssertPassInBothModes("built-ins/Number/bigint-conversion.js");

    [Fact]
    public void Number_toFixed_uses_standard_notation_threshold()
        => AssertPassInBothModes(
            "built-ins/Number/prototype/toFixed/S15.7.4.5_A1.4_T01.js");

    [Fact]
    public void Boolean_converts_eval_declaration_completion_to_false()
        => AssertPassInBothModes("built-ins/Boolean/S9.2_A1_T1.js");

    [Fact]
    public void Error_isError_recognizes_intrinsic_error_instances()
        => AssertPass(
            "built-ins/Error/isError/errors.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Error/isError/bigints.js")]
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
        => AssertPassInBothModes("built-ins/Error/length.js");

    [Fact]
    public void Error_omits_message_when_no_message_is_supplied()
        => AssertPass(
            "built-ins/Error/the-initial-value-of-errorprototypemessage-is-the-empty-string.js",
            Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Error/tostring-1.js")]
    [InlineData("built-ins/Error/tostring-2.js")]
    public void Error_instances_honor_prototype_toString_replacement(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void Error_prototype_constructor_builds_branded_instances()
        => AssertPassInBothModes(
            "built-ins/Error/prototype/constructor/S15.11.4.1_A1_T2.js");

    [Fact]
    public void Error_cause_propagates_abrupt_has_and_get_operations()
        => AssertPassInBothModes("built-ins/Error/cause_abrupt.js");

    [Fact]
    public void Error_isError_recognizes_subclasses_declared_in_feature_blocks()
        => AssertPassInBothModes("built-ins/Error/isError/error-subclass.js");

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
    [InlineData("built-ins/JSON/isRawJSON/basic.js")]
    [InlineData("built-ins/JSON/isRawJSON/builtin.js")]
    [InlineData("built-ins/JSON/isRawJSON/length.js")]
    [InlineData("built-ins/JSON/isRawJSON/name.js")]
    [InlineData("built-ins/JSON/isRawJSON/not-a-constructor.js")]
    [InlineData("built-ins/JSON/isRawJSON/prop-desc.js")]
    [InlineData("built-ins/JSON/rawJSON/basic.js")]
    [InlineData("built-ins/JSON/rawJSON/builtin.js")]
    [InlineData("built-ins/JSON/rawJSON/illegal-empty-and-start-end-chars.js")]
    [InlineData("built-ins/JSON/rawJSON/invalid-JSON-text.js")]
    [InlineData("built-ins/JSON/rawJSON/length.js")]
    [InlineData("built-ins/JSON/rawJSON/name.js")]
    [InlineData("built-ins/JSON/rawJSON/not-a-constructor.js")]
    [InlineData("built-ins/JSON/rawJSON/prop-desc.js")]
    [InlineData("built-ins/JSON/rawJSON/returns-expected-object.js")]
    public void JSON_raw_value_track_b_cases_pass_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Fact]
    public void JSON_global_object_descriptor_passes_in_both_modes()
        => AssertPassInBothModes("built-ins/JSON/prop-desc.js");

    [Theory]
    [InlineData("built-ins/JSON/parse/revived-proxy-revoked.js")]
    [InlineData("built-ins/JSON/stringify/value-array-proxy-revoked.js")]
    [InlineData("built-ins/JSON/stringify/value-object-proxy-revoked.js")]
    public void JSON_revoked_proxy_errors_pass_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Error/cause_property.js")]
    [InlineData("built-ins/Error/message_property.js")]
    public void Error_instances_expose_spec_own_descriptors(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    public static TheoryData<string> RemainingJsonCompilerParityCases => new()
    {
        "built-ins/JSON/parse/reviver-array-define-prop-err.js",
        "built-ins/JSON/parse/reviver-call-order.js",
        "built-ins/JSON/parse/reviver-object-non-configurable-prop-delete.js",
        "built-ins/JSON/stringify/property-order.js",
        "built-ins/JSON/stringify/replacer-function-arguments.js",
        "built-ins/JSON/stringify/replacer-function-result.js",
        "built-ins/JSON/stringify/replacer-function-wrapper.js",
        "built-ins/JSON/stringify/value-bigint-order.js",
        "built-ins/JSON/stringify/value-bigint-tojson-receiver.js",
        "built-ins/JSON/stringify/value-bigint.js",
        "built-ins/JSON/stringify/value-tojson-array-circular.js",
        "built-ins/JSON/stringify/value-tojson-result.js",
    };

    [Theory]
    [MemberData(nameof(RemainingJsonCompilerParityCases))]
    public void Remaining_JSON_compiler_parity(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    public static TheoryData<string> RemainingNumberCompilerParityCases => new()
    {
        "built-ins/Number/prototype/toLocaleString/length.js",
        "built-ins/Number/prototype/toLocaleString/name.js",
    };

    [Theory]
    [MemberData(nameof(RemainingNumberCompilerParityCases))]
    public void Remaining_Number_compiler_parity(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    public static TheoryData<string> Issue1380ResidualCompilerCases => new()
    {
        "built-ins/DataView/byteOffset-validated-against-initial-buffer-length.js",
        "built-ins/Set/prototype/difference/receiver-not-set.js",
        "built-ins/Set/prototype/intersection/receiver-not-set.js",
        "built-ins/Set/prototype/isDisjointFrom/receiver-not-set.js",
        "built-ins/Set/prototype/isSubsetOf/receiver-not-set.js",
        "built-ins/Set/prototype/isSupersetOf/receiver-not-set.js",
        "built-ins/Set/prototype/symmetricDifference/receiver-not-set.js",
        "built-ins/Set/prototype/union/receiver-not-set.js",
    };

    [Theory]
    [MemberData(nameof(Issue1380ResidualCompilerCases))]
    public void Issue_1380_residuals_match_in_compiled_mode(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);
}
