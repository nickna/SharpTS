using Xunit;

namespace SharpTS.Test262;

public sealed partial class RuntimeConformanceTests
{

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

    /// <summary>ECMA-262 §9.4.2: resolving an unbound name throws a ReferenceError.</summary>
    [Theory]
    [InlineData("built-ins/Array/prototype/filter/15.4.4.20-4-2.js")]
    [InlineData("built-ins/Array/prototype/forEach/15.4.4.18-4-2.js")]
    [InlineData("built-ins/Array/prototype/map/15.4.4.19-4-2.js")]
    public void Unresolvable_names_throw_ReferenceError(string relativePath)
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
    public void Array_length_growth_does_not_materialize_prototype_indices()
        => AssertPass(
            "built-ins/Array/length/S15.4.5.1_A1.2_T3.js",
            Test262ExecutionMode.Interpreted);

    [Fact]
    public void Array_from_propagates_iterator_getter_errors()
        => AssertPassInBothModes("built-ins/Array/from/get-iter-method-err.js");

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
    [InlineData("built-ins/Array/prototype/push/throws-with-string-receiver.js")]
    [InlineData("built-ins/Array/prototype/pop/throws-with-string-receiver.js")]
    [InlineData("built-ins/Array/prototype/unshift/throws-with-string-receiver.js")]
    [InlineData("built-ins/Array/prototype/fill/fill-values.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-this-length.js")]
    [InlineData("built-ins/Array/prototype/copyWithin/return-abrupt-from-this-length.js")]
    public void Array_mutators_called_generically_update_the_original_receiver(string relativePath)
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
    [InlineData("built-ins/Array/prototype/toSorted/comparefn-called-after-get-elements.js")]
    [InlineData("built-ins/Array/prototype/toSorted/comparefn-stop-after-error.js")]
    public void Compiled_copying_sort_preserves_abrupt_completion_order(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

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
    [InlineData("built-ins/Array/prototype/shift/S15.4.4.9_A1.2_T1.js")]
    [InlineData("built-ins/Array/prototype/toSorted/holes-not-preserved.js")]
    [InlineData("built-ins/Array/prototype/toSorted/length-decreased-while-iterating.js")]
    public void Array_mutators_and_copying_sort_preserve_sparse_semantics(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/indexOf/15.4.4.14-9-a-19.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-19.js")]
    public void Array_search_preserves_nonconfigurable_tail_elements(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Array/prototype/concat/arg-length-near-integer-limit.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-this-length-as-symbol.js")]
    [InlineData("built-ins/Array/prototype/fill/return-abrupt-from-this-length.js")]
    [InlineData("built-ins/Array/prototype/filter/15.4.4.20-9-b-6.js")]
    [InlineData("built-ins/Array/prototype/indexOf/15.4.4.14-9-b-i-22.js")]
    [InlineData("built-ins/Array/prototype/indexOf/15.4.4.14-9-b-ii-2.js")]
    [InlineData("built-ins/Array/prototype/indexOf/calls-only-has-on-prototype-after-length-zeroed.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-b-i-22.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-b-ii-2.js")]
    [InlineData("built-ins/Array/prototype/lastIndexOf/calls-only-has-on-prototype-after-length-zeroed.js")]
    public void Remaining_Array_interpreter_parity(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> RemainingArrayCompilerParityCases => new()
    {
        "built-ins/Array/S15.4_A1.1_T9.js",
        "built-ins/Array/from/array-like-has-length-but-no-indexes-with-values.js",
        "built-ins/Array/from/items-is-arraybuffer.js",
        "built-ins/Array/from/iter-map-fn-err.js",
        "built-ins/Array/prototype/concat/Array.prototype.concat_sloppy-arguments-with-dupes.js",
        "built-ins/Array/prototype/concat/Array.prototype.concat_sloppy-arguments.js",
        "built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-boolean-wrapper.js",
        "built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-number-wrapper.js",
        "built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-reg-exp.js",
        "built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-string-wrapper.js",
        "built-ins/Array/prototype/concat/Array.prototype.concat_strict-arguments.js",
        "built-ins/Array/prototype/concat/S15.4.4.4_A3_T1.js",
        "built-ins/Array/prototype/concat/arg-length-exceeding-integer-limit.js",
        "built-ins/Array/prototype/concat/create-revoked-proxy.js",
        "built-ins/Array/prototype/concat/is-concat-spreadable-is-array-proxy-revoked.js",
        "built-ins/Array/prototype/concat/is-concat-spreadable-proxy-revoked.js",
        "built-ins/Array/prototype/concat/is-concat-spreadable-proxy.js",
        "built-ins/Array/prototype/entries/iteration-mutable.js",
        "built-ins/Array/prototype/every/15.4.4.16-7-b-15.js",
        "built-ins/Array/prototype/every/15.4.4.16-7-b-16.js",
        "built-ins/Array/prototype/filter/15.4.4.20-1-12.js",
        "built-ins/Array/prototype/filter/15.4.4.20-9-b-15.js",
        "built-ins/Array/prototype/filter/15.4.4.20-9-b-16.js",
        "built-ins/Array/prototype/filter/create-revoked-proxy.js",
        "built-ins/Array/prototype/find/return-abrupt-from-this-length.js",
        "built-ins/Array/prototype/findLast/return-abrupt-from-this-length.js",
        "built-ins/Array/prototype/flat/bound-function-call.js",
        "built-ins/Array/prototype/flat/null-undefined-input-throws.js",
        "built-ins/Array/prototype/flatMap/array-like-objects.js",
        "built-ins/Array/prototype/forEach/15.4.4.18-7-b-15.js",
        "built-ins/Array/prototype/forEach/15.4.4.18-7-b-16.js",
        "built-ins/Array/prototype/includes/this-is-not-object.js",
        "built-ins/Array/prototype/includes/tolength-length.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-9-b-i-20.js",
        "built-ins/Array/prototype/indexOf/calls-only-has-on-prototype-after-length-zeroed.js",
        "built-ins/Array/prototype/join/S15.4.4.5_A3.1_T1.js",
        "built-ins/Array/prototype/keys/iteration-mutable.js",
        "built-ins/Array/prototype/lastIndexOf/calls-only-has-on-prototype-after-length-zeroed.js",
        "built-ins/Array/prototype/map/15.4.4.19-8-b-15.js",
        "built-ins/Array/prototype/map/15.4.4.19-8-b-16.js",
        "built-ins/Array/prototype/map/15.4.4.19-8-c-i-20.js",
        "built-ins/Array/prototype/map/create-revoked-proxy.js",
        "built-ins/Array/prototype/methods-called-as-functions.js",
        "built-ins/Array/prototype/reverse/S15.4.4.8_A4_T1.js",
        "built-ins/Array/prototype/reverse/get_if_present_with_delete.js",
        "built-ins/Array/prototype/reverse/length-exceeding-integer-limit-with-proxy.js",
        "built-ins/Array/prototype/shift/throws-when-this-value-length-is-writable-false.js",
        "built-ins/Array/prototype/slice/create-revoked-proxy.js",
        "built-ins/Array/prototype/slice/length-exceeding-integer-limit-proxied-array.js",
        "built-ins/Array/prototype/slice/length-exceeding-integer-limit.js",
        "built-ins/Array/prototype/some/15.4.4.17-7-b-15.js",
        "built-ins/Array/prototype/some/15.4.4.17-7-b-16.js",
        "built-ins/Array/prototype/sort/S15.4.4.11_A3_T1.js",
        "built-ins/Array/prototype/sort/S15.4.4.11_A3_T2.js",
        "built-ins/Array/prototype/sort/S15.4.4.11_A4_T3.js",
        "built-ins/Array/prototype/sort/S15.4.4.11_A6_T2.js",
        "built-ins/Array/prototype/sort/S15.4.4.11_A8.js",
        "built-ins/Array/prototype/sort/call-with-primitive.js",
        "built-ins/Array/prototype/sort/precise-prototype-accessors.js",
        "built-ins/Array/prototype/splice/S15.4.4.12_A4_T1.js",
        "built-ins/Array/prototype/splice/S15.4.4.12_A4_T2.js",
        "built-ins/Array/prototype/splice/S15.4.4.12_A4_T3.js",
        "built-ins/Array/prototype/splice/create-revoked-proxy.js",
        "built-ins/Array/prototype/toReversed/get-descending-order.js",
        "built-ins/Array/prototype/toSpliced/discarded-element-not-read.js",
        "built-ins/Array/prototype/toSpliced/elements-read-in-order.js",
        "built-ins/Array/prototype/toSpliced/length-clamped-to-2pow53minus1.js",
        "built-ins/Array/prototype/toSpliced/length-exceeding-array-length-limit.js",
        "built-ins/Array/prototype/values/iteration-mutable.js",
    };

    [Theory]
    [MemberData(nameof(RemainingArrayCompilerParityCases))]
    public void Remaining_Array_compiler_parity(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);
}
