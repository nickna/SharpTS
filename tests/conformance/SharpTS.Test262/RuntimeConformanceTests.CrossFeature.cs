using Xunit;

namespace SharpTS.Test262;

public sealed partial class RuntimeConformanceTests
{

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

    /// <summary>
    /// Issue #1326: Error constructors used to live in the process-static globals table.
    /// A test that replaced Error.prototype.toString therefore changed the callable and
    /// descriptor observed by later Interpreter instances in the same persistent worker.
    /// </summary>
    [Fact]
    public void Error_prototype_mutation_does_not_outlive_the_program()
    {
        var root = Test262Paths.RequireRoot();

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
    [InlineData("built-ins/Object/defineProperty/symbol-data-property-default-non-strict.js")]
    [InlineData("built-ins/Object/defineProperty/symbol-data-property-default-strict.js")]
    [InlineData("built-ins/Object/defineProperty/symbol-data-property-writable.js")]
    [InlineData("built-ins/Array/prototype/concat/is-concat-spreadable-get-err.js")]
    public void Compiled_symbol_descriptors_preserve_property_semantics(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    [Theory]
    [InlineData("language/expressions/prefix-increment/bigint.js")]
    [InlineData("language/expressions/prefix-decrement/bigint.js")]
    [InlineData("language/expressions/postfix-increment/bigint.js")]
    [InlineData("language/expressions/postfix-decrement/bigint.js")]
    [InlineData("built-ins/BigInt/prototype/toString/a-z.js")]
    public void BigInt_update_operators_preserve_bigint_values(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("built-ins/Boolean/prop-desc.js")]
    [InlineData("built-ins/Array/prop-desc.js")]
    [InlineData("built-ins/Error/prop-desc.js")]
    [InlineData("built-ins/Math/prop-desc.js")]
    [InlineData("built-ins/Number/prop-desc.js")]
    [InlineData("built-ins/Object/prop-desc.js")]
    [InlineData("built-ins/String/prop-desc.js")]
    public void Built_in_global_bindings_have_standard_descriptors(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Boolean/prototype/S15.6.3.1_A3.js")]
    [InlineData("built-ins/Boolean/prototype/S15.6.3.1_A4.js")]
    [InlineData("built-ins/Error/prototype/S15.11.3.1_A2_T1.js")]
    [InlineData("built-ins/Number/MAX_VALUE/S15.7.3.2_A4.js")]
    [InlineData("built-ins/Number/MIN_VALUE/S15.7.3.3_A4.js")]
    [InlineData("built-ins/Object/prototype/S15.2.3.1_A2.js")]
    [InlineData("built-ins/RegExp/prototype/S15.10.5.1_A2.js")]
    [InlineData("built-ins/String/prototype/S15.5.3.1_A2.js")]
    public void Built_in_constructor_properties_are_non_configurable_and_non_enumerable(
        string relativePath)
        => AssertPassInBothModes(relativePath);

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
    [InlineData("built-ins/Object/defineProperty/15.2.3.6-4-243-2.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-180.js")]
    [InlineData("built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-4.js")]
    [InlineData("built-ins/Object/preventExtensions/15.2.3.10-3-19.js")]
    [InlineData("built-ins/Object/preventExtensions/15.2.3.10-3-9.js")]
    [InlineData("built-ins/Object/prototype/toString/symbol-tag-non-str-bigint.js")]
    [InlineData("built-ins/JSON/Symbol.toStringTag.js")]
    [InlineData("language/expressions/property-accessors/S11.2.1_A4_T4.js")]
    public void Remaining_object_and_intrinsic_interpreter_parity(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> ExposedNonPromiseInterpreterDeficitCases => new()
    {
        "built-ins/Array/from/elements-deleted-after.js",
        "built-ins/Array/from/source-array-boundary.js",
        "built-ins/Array/isArray/proxy-revoked.js",
        "built-ins/Array/isArray/proxy.js",
        "built-ins/Array/prototype/toSorted/comparefn-not-a-function.js",
        "built-ins/JSON/parse/reviver-object-define-prop-err.js",
        "built-ins/JSON/parse/reviver-object-non-configurable-prop-create.js",
        "built-ins/Object/assign/strings-and-symbol-order-proxy.js",
        "built-ins/Object/assign/target-is-non-extensible-existing-accessor-property.js",
        "built-ins/Object/assign/target-is-sealed-existing-accessor-property.js",
        "built-ins/Object/assign/target-set-user-error.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-24.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-145-1.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-171-1.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-224-1.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-254-1.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-39-1.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-92-1.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-410.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-584.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-586.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-594.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-596.js",
        "built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-187.js",
        "built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-188.js",
        "built-ins/Object/getPrototypeOf/15.2.3.2-2-25.js",
        "built-ins/Object/groupBy/iterator-next-throws.js",
        "built-ins/Object/preventExtensions/15.2.3.10-3-18.js",
        "built-ins/Object/preventExtensions/15.2.3.10-3-8.js",
        "built-ins/Object/prototype/toString/Object.prototype.toString.call-bigint.js",
        "built-ins/Object/prototype/toString/symbol-tag-override-bigint.js",
        "built-ins/RegExp/CharacterClassEscapes/character-class-word-class-escape-negative-cases.js",
        "built-ins/RegExp/prototype/Symbol.matchAll/isregexp-called-once.js",
    };

    [Theory]
    [MemberData(nameof(ExposedNonPromiseInterpreterDeficitCases))]
    public void Exposed_non_Promise_interpreter_deficits_pass_in_both_modes(string relativePath)
    {
        // This generated RegExp case builds a million-code-point string. Its
        // compiled coverage remains in the pooled committed-baseline run; doing
        // a second in-process compiled execution inside this interpreter-gap
        // theory becomes allocation-bound when all 81 rows execute together.
        if (relativePath.EndsWith(
                "character-class-word-class-escape-negative-cases.js",
                StringComparison.Ordinal))
        {
            AssertPass(
                relativePath,
                Test262ExecutionMode.Interpreted,
                TimeSpan.FromSeconds(60));
            return;
        }

        AssertPassInBothModes(relativePath);
    }

    [Theory]
    [InlineData("built-ins/Promise/all/iter-arg-is-number-reject.js")]
    [InlineData("built-ins/Promise/any/iter-returns-number-reject.js")]
    [InlineData("built-ins/Promise/any/iter-returns-null-reject.js")]
    [InlineData("built-ins/Promise/all/capability-resolve-throws-no-close.js")]
    [InlineData("built-ins/Promise/allSettled/capability-resolve-throws-no-close.js")]
    [InlineData("built-ins/Promise/all/resolve-from-same-thenable.js")]
    [InlineData("built-ins/JSON/parse/revived-proxy.js")]
    [InlineData("built-ins/JSON/parse/reviver-array-delete-err.js")]
    [InlineData("built-ins/JSON/parse/reviver-array-length-get-err.js")]
    [InlineData("built-ins/Object/prototype/toString/symbol-tag-non-str-bigint.js")]
    [InlineData("built-ins/Promise/allKeyed/symbol-keys.js")]
    [InlineData("built-ins/Promise/allKeyed/arg-is-function.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/prototype-keys-ignored.js")]
    public void Issue1279_regression_guards_pass_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> ClassAsyncAndGeneratorMethodCompilerCases => new()
    {
        "language/statements/class/async-method/dflt-params-arg-val-not-undefined.js",
        "language/expressions/class/async-method/dflt-params-arg-val-not-undefined.js",
        "language/statements/class/async-method-static/dflt-params-arg-val-not-undefined.js",
        "language/expressions/class/async-method-static/dflt-params-arg-val-not-undefined.js",
        "language/statements/class/gen-method-static/dflt-params-abrupt.js",
        "language/expressions/class/gen-method-static/dflt-params-abrupt.js",
        "language/statements/class/gen-method/dflt-params-ref-later.js",
        "language/expressions/class/gen-method/dflt-params-ref-later.js",
        "language/statements/class/async-method/params-trailing-comma-single.js",
        "language/expressions/class/async-method/params-trailing-comma-single.js",
        "language/statements/class/gen-method/params-trailing-comma-single.js",
        "language/expressions/class/gen-method/params-trailing-comma-single.js",
        "language/statements/class/gen-method-static/params-trailing-comma-single.js",
        "language/expressions/class/gen-method-static/params-trailing-comma-single.js",
        "language/statements/class/gen-method-static/params-trailing-comma-multiple.js",
        "language/expressions/class/gen-method-static/params-trailing-comma-multiple.js",
        "language/statements/class/gen-method-static/dflt-params-trailing-comma.js",
        "language/expressions/class/gen-method-static/dflt-params-trailing-comma.js",
        "language/statements/class/gen-method/yield-spread-obj.js",
        "language/expressions/class/gen-method/yield-spread-obj.js",
        "language/statements/class/async-method/forbidden-ext/b2/cls-decl-async-meth-forbidden-ext-indirect-access-prop-caller.js",
        "language/expressions/class/async-method/forbidden-ext/b2/cls-expr-async-meth-forbidden-ext-indirect-access-prop-caller.js",
    };

    [Theory]
    [MemberData(nameof(ClassAsyncAndGeneratorMethodCompilerCases))]
    public void Class_async_and_generator_methods_compile_with_runtime_prototypes(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    public static TheoryData<string> Issue1374ClassResidualCases => new()
    {
        "language/expressions/class/cpn-class-expr-computed-property-name-from-yield-expression.js",
        "language/expressions/class/elements/indirect-eval-contains-arguments.js",
        "language/expressions/class/elements/nested-indirect-eval-contains-arguments.js",
        "language/expressions/class/elements/prod-private-method-before-super-return-in-field-initializer.js",
        "language/expressions/class/scope-gen-meth-paramsbody-var-close.js",
        "language/expressions/class/scope-static-gen-meth-paramsbody-var-close.js",
        "language/statements/class/cpn-class-decl-computed-property-name-from-yield-expression.js",
        "language/statements/class/elements/indirect-eval-contains-arguments.js",
        "language/statements/class/elements/nested-indirect-eval-contains-arguments.js",
        "language/statements/class/elements/prod-private-method-before-super-return-in-field-initializer.js",
        "language/statements/class/scope-gen-meth-paramsbody-var-close.js",
        "language/statements/class/scope-static-gen-meth-paramsbody-var-close.js",
        "language/statements/class/subclass/derived-class-return-override-catch-finally-arrow.js",
        "language/statements/class/subclass/derived-class-return-override-catch-finally.js",
        "language/statements/class/subclass/derived-class-return-override-finally-super-arrow.js",
        "language/statements/class/subclass/derived-class-return-override-finally-super.js",
        "language/statements/class/subclass/derived-class-return-override-for-of-arrow.js",
        "language/expressions/class/dstr/meth-ary-ptrn-elision-step-err.js",
        "language/expressions/class/dstr/meth-ary-ptrn-rest-id-iter-step-err.js",
        "language/expressions/class/dstr/meth-dflt-ary-ptrn-elision-step-err.js",
        "language/expressions/class/dstr/meth-dflt-ary-ptrn-rest-id-iter-step-err.js",
        "language/expressions/class/dstr/meth-static-ary-ptrn-elision-step-err.js",
        "language/expressions/class/dstr/meth-static-ary-ptrn-rest-id-iter-step-err.js",
        "language/expressions/class/dstr/meth-static-dflt-ary-ptrn-elision-step-err.js",
        "language/expressions/class/dstr/meth-static-dflt-ary-ptrn-rest-id-iter-step-err.js",
        "language/statements/class/dstr/meth-ary-ptrn-elision-step-err.js",
        "language/statements/class/dstr/meth-ary-ptrn-rest-id-iter-step-err.js",
        "language/statements/class/dstr/meth-dflt-ary-ptrn-elision-step-err.js",
        "language/statements/class/dstr/meth-dflt-ary-ptrn-rest-id-iter-step-err.js",
        "language/statements/class/dstr/meth-static-ary-ptrn-elision-step-err.js",
        "language/statements/class/dstr/meth-static-ary-ptrn-rest-id-iter-step-err.js",
        "language/statements/class/dstr/meth-static-dflt-ary-ptrn-elision-step-err.js",
        "language/statements/class/dstr/meth-static-dflt-ary-ptrn-rest-id-iter-step-err.js",
        "language/expressions/class/subclass-builtins/subclass-AggregateError.js",
        "language/expressions/class/subclass-builtins/subclass-Array.js",
        "language/expressions/class/subclass-builtins/subclass-Error.js",
        "language/expressions/class/subclass-builtins/subclass-EvalError.js",
        "language/expressions/class/subclass-builtins/subclass-Promise.js",
        "language/expressions/class/subclass-builtins/subclass-RangeError.js",
        "language/expressions/class/subclass-builtins/subclass-ReferenceError.js",
        "language/expressions/class/subclass-builtins/subclass-SyntaxError.js",
        "language/expressions/class/subclass-builtins/subclass-TypeError.js",
        "language/expressions/class/subclass-builtins/subclass-URIError.js",
        "language/statements/class/subclass/builtin-objects/Array/length.js",
        "language/statements/class/subclass/builtin-objects/Array/regular-subclassing.js",
        "language/statements/class/subclass/builtin-objects/Error/message-property-assignment.js",
        "language/statements/class/subclass/builtin-objects/NativeError/EvalError-message.js",
        "language/statements/class/subclass/builtin-objects/NativeError/RangeError-message.js",
        "language/statements/class/subclass/builtin-objects/NativeError/ReferenceError-message.js",
        "language/statements/class/subclass/builtin-objects/NativeError/SyntaxError-message.js",
        "language/statements/class/subclass/builtin-objects/NativeError/TypeError-message.js",
        "language/statements/class/subclass/builtin-objects/NativeError/URIError-message.js",
        "language/expressions/class/elements/syntax/valid/grammar-special-prototype-async-meth-valid.js",
        "language/expressions/class/elements/syntax/valid/grammar-special-prototype-gen-meth-valid.js",
        "language/expressions/class/elements/syntax/valid/grammar-special-prototype-meth-valid.js",
        "language/statements/class/elements/syntax/valid/grammar-special-prototype-async-meth-valid.js",
        "language/statements/class/elements/syntax/valid/grammar-special-prototype-gen-meth-valid.js",
        "language/statements/class/elements/syntax/valid/grammar-special-prototype-meth-valid.js",
        "language/statements/class/definition/constructor-property.js",
        "language/expressions/class/poisoned-underscore-proto.js",
        "language/statements/class/poisoned-underscore-proto.js",
        "language/expressions/class/elements/syntax/valid/grammar-static-ctor-async-meth-valid.js",
        "language/expressions/class/elements/syntax/valid/grammar-static-ctor-meth-valid.js",
        "language/statements/class/elements/syntax/valid/grammar-static-ctor-accessor-meth-valid.js",
        "language/statements/class/elements/syntax/valid/grammar-static-ctor-async-meth-valid.js",
        "language/statements/class/elements/syntax/valid/grammar-static-ctor-meth-valid.js",
        "language/statements/class/definition/constructor-strict-by-default.js",
        "language/statements/class/definition/constructor.js",
        "language/statements/class/definition/getters-restricted-ids.js",
        "language/statements/class/definition/setters-restricted-ids.js",
        "language/statements/class/definition/side-effects-in-extends.js",
        "language/statements/class/name-binding/in-extends-expression.js",
        "language/statements/class/name-binding/in-extends-expression-assigned.js",
        "language/statements/class/name-binding/in-extends-expression-grouped.js",
        "language/statements/class/arguments/access.js",
        "language/statements/class/arguments/default-constructor.js",
        "language/statements/class/subclass/class-definition-evaluation-empty-constructor-heritage-present.js",
        "language/statements/class/elements/after-same-line-static-async-method-private-names.js",
        "language/statements/class/elements/after-same-line-static-async-method-static-private-fields.js",
        "language/statements/class/elements/async-private-method-static/returns-async-arrow.js",
        "language/statements/class/elements/async-private-method-static/returns-async-function.js",
        "language/statements/class/elements/async-private-method/returns-async-arrow.js",
        "language/statements/class/elements/async-private-method/returns-async-function.js",
        "language/statements/class/elements/nested-private-indirect-eval-contains-arguments.js",
        "language/statements/class/elements/private-field-on-nested-class.js",
        "language/statements/class/elements/private-indirect-eval-contains-arguments.js",
        "language/statements/class/elements/private-method-is-not-a-own-property.js",
        "language/statements/class/elements/private-method-on-nested-class.js",
        "language/statements/class/elements/private-static-field-usage-inside-nested-class.js",
        "language/statements/class/elements/privatefieldget-success-1.js",
        "language/statements/class/elements/privatefieldget-typeerror-2.js",
        "language/statements/class/elements/privatefieldget-typeerror-4.js",
        "language/statements/class/elements/privatefieldset-evaluation-order-2.js",
        "language/statements/class/elements/privatename-valid-no-earlyerr.js",
        "language/statements/class/elements/regular-definitions-static-private-methods.js",
        "language/statements/class/elements/static-private-fields-proxy-default-handler-throws.js",
    };

    [Theory]
    [MemberData(nameof(Issue1374ClassResidualCases))]
    public void Issue_1374_class_residuals_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> ClassMethodForbiddenExtensionCompilerCases => new()
    {
        "language/statements/class/method-static/forbidden-ext/b1/cls-decl-meth-static-forbidden-ext-direct-access-prop-arguments.js",
        "language/expressions/class/method-static/forbidden-ext/b1/cls-expr-meth-static-forbidden-ext-direct-access-prop-arguments.js",
        "language/statements/class/method-static/forbidden-ext/b1/cls-decl-meth-static-forbidden-ext-direct-access-prop-caller.js",
        "language/expressions/class/method-static/forbidden-ext/b1/cls-expr-meth-static-forbidden-ext-direct-access-prop-caller.js",
        "language/statements/class/gen-method-static/forbidden-ext/b1/cls-decl-gen-meth-static-forbidden-ext-direct-access-prop-arguments.js",
        "language/expressions/class/gen-method-static/forbidden-ext/b1/cls-expr-gen-meth-static-forbidden-ext-direct-access-prop-arguments.js",
        "language/statements/class/gen-method-static/forbidden-ext/b1/cls-decl-gen-meth-static-forbidden-ext-direct-access-prop-caller.js",
        "language/expressions/class/gen-method-static/forbidden-ext/b1/cls-expr-gen-meth-static-forbidden-ext-direct-access-prop-caller.js",
        "language/statements/class/async-method-static/forbidden-ext/b1/cls-decl-async-meth-static-forbidden-ext-direct-access-prop-arguments.js",
        "language/expressions/class/async-method-static/forbidden-ext/b1/cls-expr-async-meth-static-forbidden-ext-direct-access-prop-arguments.js",
        "language/statements/class/async-method-static/forbidden-ext/b1/cls-decl-async-meth-static-forbidden-ext-direct-access-prop-caller.js",
        "language/expressions/class/async-method-static/forbidden-ext/b1/cls-expr-async-meth-static-forbidden-ext-direct-access-prop-caller.js",
    };

    [Theory]
    [MemberData(nameof(ClassMethodForbiddenExtensionCompilerCases))]
    public void Static_class_method_function_objects_match_in_compiled_mode(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    public static TheoryData<string> DynamicImportCompilerCases => new()
    {
        "language/expressions/dynamic-import/always-create-new-promise.js",
        "language/expressions/dynamic-import/assignment-expression/unary-expr.js",
        "language/expressions/dynamic-import/catch/top-level-import-catch-file-does-not-exist.js",
        "language/expressions/dynamic-import/catch/nested-async-function-await-file-does-not-exist.js",
        "language/expressions/dynamic-import/catch/nested-async-function-file-does-not-exist.js",
        "language/expressions/dynamic-import/catch/nested-async-function-return-await-file-does-not-exist.js",
        "language/expressions/dynamic-import/syntax/valid/callexpression-arguments.js",
        "language/expressions/dynamic-import/syntax/valid/nested-block-nested-imports.js",
        "language/expressions/dynamic-import/syntax/valid/top-level-script-code-valid.js",
    };

    [Theory]
    [MemberData(nameof(DynamicImportCompilerCases))]
    public void Dynamic_import_call_shapes_and_rejections_match_in_compiled_mode(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    public static TheoryData<string> RemainingBuiltInTailCompilerCases => new()
    {
            "built-ins/Array/prototype/concat/Array.prototype.concat_spreadable-function.js",
        "built-ins/Object/assign/target-is-frozen-accessor-property-set-succeeds.js",
        "built-ins/Object/prototype/toString/symbol-tag-non-str-proxy-function.js",
        "built-ins/String/S15.5.2.1_A1_T8.js",
    };

    [Theory]
    [MemberData(nameof(RemainingBuiltInTailCompilerCases))]
    public void Remaining_committed_built_in_tail_matches_in_compiled_mode(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    public static TheoryData<string> Issue1374ReproducedCompilerRegressionCases => new()
    {
        "built-ins/Array/prototype/reverse/length-exceeding-integer-limit-with-proxy.js",
        "built-ins/Promise/all/same-reject-function.js",
        "built-ins/Promise/any/invoke-then-on-promises-every-iteration.js",
        "language/expressions/new/S11.2.2_A3_T5.js",
    };

    [Theory]
    [MemberData(nameof(Issue1374ReproducedCompilerRegressionCases))]
    public void Issue_1374_reproduced_compiler_regressions_pass(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    public static TheoryData<string> Issue1382LanguageExpressionCases => new()
    {
        // Primitive coercion, numeric conversion, equality, and update operators.
        "language/expressions/addition/coerce-symbol-to-prim-invocation.js",
        "language/expressions/left-shift/S9.5_A2.1_T1.js",
        "language/expressions/equals/bigint-and-object.js",
        "language/expressions/greater-than-or-equal/bigint-and-non-finite.js",
        "language/expressions/postfix-increment/bigint.js",
        "language/types/number/S8.5_A5.js",
        "built-ins/Array/prototype/reduce/15.4.4.21-9-c-i-32.js",
        "built-ins/Number/S9.3.1_A3_T2.js",

        // References, strict assignment, destructuring, and lexical initialization.
        "language/expressions/logical-assignment/lgcl-and-assignment-operator-unresolved-lhs.js",
        "language/expressions/assignment/dstr/obj-rest-number.js",
        "language/expressions/assignment/dstr/array-elem-init-let.js",

        // Callable metadata, parameter environments, and named/self bindings.
        "language/expressions/arrow-function/name.js",
        "language/expressions/arrow-function/prototype-rules.js",
        "language/expressions/arrow-function/dflt-params-abrupt.js",
        "language/expressions/arrow-function/scope-param-elem-var-close.js",
        "language/expressions/function/name.js",
        "language/expressions/generators/scope-name-var-close.js",
        "language/expressions/generators/scope-paramsbody-var-close.js",

        // Suspension, completion routing, and receiver preservation.
        "language/expressions/yield/star-string.js",
        "language/expressions/yield/rhs-unresolvable.js",
        "language/expressions/async-function/try-return-finally-throw.js",
        "language/expressions/optional-chaining/member-expression-async-this.js",
        "language/expressions/optional-chaining/optional-call-preserves-this.js",

        // Computed keys, iterator-based spread, strictness, and eval discovery.
        "language/computed-property-names/class/accessor/getter-duplicates.js",
        "language/expressions/object/accessor-name-computed-err-to-prop-key.js",
        "language/expressions/object/method-definition/generator-prop-name-eval-error.js",
        "language/expressions/super/call-spread-err-mult-err-iter-get-value.js",
        "language/expressions/tagged-template/call-expression-context-strict.js",
        "language/literals/regexp/mongolian-vowel-separator-eval.js",
    };

    [Theory]
    [MemberData(nameof(Issue1382LanguageExpressionCases))]
    public void Issue_1382_language_expression_abstractions_match_in_both_modes(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> Issue1429CompiledRuntimeBoundaryCases => new()
    {
        "built-ins/Array/prototype/every/15.4.4.16-7-c-iii-28.js",
        "built-ins/Array/prototype/fill/return-abrupt-from-setting-property-value.js",
        "built-ins/Array/prototype/indexOf/15.4.4.14-9-a-2.js",
        "built-ins/Array/prototype/indexOf/length-near-integer-limit.js",
        "built-ins/Array/prototype/lastIndexOf/15.4.4.15-8-a-2.js",
        "built-ins/Array/prototype/lastIndexOf/length-near-integer-limit.js",
        "built-ins/Array/prototype/push/length-near-integer-limit-set-failure.js",
        "built-ins/Array/prototype/reduceRight/15.4.4.22-9-b-17.js",
        "built-ins/Array/prototype/reduceRight/15.4.4.22-9-b-4.js",
        "built-ins/Array/prototype/reduceRight/15.4.4.22-9-c-i-31.js",
        "built-ins/Array/prototype/some/15.4.4.17-7-c-iii-28.js",
        "built-ins/JSON/stringify/replacer-function-arguments.js",
        "built-ins/Object/create/15.2.3.5-4-258.js",
        "built-ins/Object/create/15.2.3.5-4-293.js",
        "built-ins/Object/defineProperties/15.2.3.7-5-b-218.js",
        "built-ins/Object/defineProperties/15.2.3.7-5-b-253.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-105.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-121.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-122.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-132.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-135.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-141.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-148.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-248.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-43.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-74.js",
        "built-ins/Object/defineProperties/15.2.3.7-6-a-95.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-13.js",
        "built-ins/Object/defineProperty/15.2.3.6-3-8.js",
        "built-ins/Object/defineProperty/15.2.3.6-4-20.js",
        "built-ins/Object/preventExtensions/15.2.3.10-3-23.js",
        "built-ins/RegExp/prototype/Symbol.search/set-lastindex-restore-err.js",
        "language/expressions/call/spread-mult-obj-ident.js",
        "language/expressions/call/spread-obj-mult-spread.js",
        "language/expressions/call/spread-obj-override-immutable.js",
        "language/expressions/call/spread-obj-overrides-prev-properties.js",
        "language/expressions/call/spread-sngl-obj-ident.js",
        "language/expressions/new/spread-mult-obj-ident.js",
        "language/expressions/new/spread-obj-mult-spread.js",
        "language/expressions/new/spread-obj-override-immutable.js",
        "language/expressions/new/spread-obj-overrides-prev-properties.js",
        "language/expressions/new/spread-sngl-obj-ident.js",
    };

    [Theory]
    [MemberData(nameof(Issue1429CompiledRuntimeBoundaryCases))]
    public void Issue_1429_compiled_runtime_boundaries_preserve_ordinary_object_semantics(
        string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);
}
