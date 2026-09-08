using Xunit;

namespace SharpTS.Test262;

public sealed partial class RuntimeConformanceTests
{

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

    [Theory]
    [InlineData("language/expressions/new/spread-mult-empty.js")]
    [InlineData("language/expressions/new/spread-err-mult-err-iter-get-value.js")]
    [InlineData("language/expressions/new/spread-err-sngl-err-itr-get-value.js")]
    public void New_function_expression_spreads_pass_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("language/expressions/less-than/bigint-and-number.js")]
    [InlineData("language/expressions/less-than-or-equal/bigint-and-number.js")]
    [InlineData("language/expressions/greater-than/bigint-and-number.js")]
    [InlineData("language/expressions/greater-than-or-equal/bigint-and-number.js")]
    public void BigInt_relational_comparison_accepts_numbers(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Interpreted);

    [Theory]
    [InlineData("language/expressions/call/spread-mult-obj-null.js")]
    [InlineData("language/expressions/call/spread-mult-obj-undefined.js")]
    [InlineData("language/expressions/call/spread-obj-null.js")]
    [InlineData("language/expressions/call/spread-obj-undefined.js")]
    public void Object_spread_ignores_nullish_sources(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("language/expressions/call/11.2.3-3_5.js")]
    [InlineData("language/expressions/call/eval-first-arg.js")]
    public void Static_direct_eval_observes_caller_bindings_and_argument_order(string relativePath)
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
    [InlineData("language/expressions/call/spread-err-mult-err-itr-get-get.js")]
    [InlineData("language/expressions/call/spread-err-sngl-err-itr-get-get.js")]
    [InlineData("language/expressions/new/spread-err-mult-err-itr-get-get.js")]
    [InlineData("language/expressions/new/spread-err-sngl-err-itr-get-get.js")]
    public void Spread_observes_symbol_iterator_accessors(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> ReferenceCompletionAndIteratorCloseCases => new()
    {
        "language/expressions/assignment/target-member-computed-reference.js",
        "language/expressions/compound-assignment/11.13.2-1-s.js",
        "language/expressions/compound-assignment/S11.13.2_A4.6_T1.1.js",
        "language/expressions/compound-assignment/S11.13.2_A7.8_T1.js",
        "language/statements/try/12.14-7.js",
        "language/statements/try/completion-values-fn-finally-return.js",
        "language/statements/for-of/iterator-close-via-break.js",
        "language/statements/for-of/iterator-close-via-throw.js",
        "language/statements/for-of/iterator-close-via-return.js",
        "language/statements/for-of/iterator-close-non-throw-get-method-abrupt.js",
        "language/statements/for-of/break-from-finally.js",
        "language/statements/for-of/return-from-finally.js",
    };

    [Theory]
    [MemberData(nameof(ReferenceCompletionAndIteratorCloseCases))]
    public void Reference_completion_and_iterator_close_match_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> FunctionEnvironmentLoweringCompilerCases => new()
    {
        "language/arguments-object/func-decl-args-trailing-comma-spread-operator.js",
        "language/arguments-object/cls-decl-meth-static-args-trailing-comma-multiple.js",
        "language/arguments-object/cls-decl-meth-static-args-trailing-comma-null.js",
        "language/arguments-object/cls-decl-meth-static-args-trailing-comma-single-args.js",
        "language/arguments-object/cls-decl-meth-static-args-trailing-comma-spread-operator.js",
        "language/arguments-object/cls-decl-meth-static-args-trailing-comma-undefined.js",
        "language/arguments-object/cls-expr-meth-args-trailing-comma-multiple.js",
        "language/arguments-object/cls-expr-meth-args-trailing-comma-null.js",
        "language/arguments-object/cls-expr-meth-args-trailing-comma-single-args.js",
        "language/arguments-object/cls-expr-meth-args-trailing-comma-spread-operator.js",
        "language/arguments-object/cls-expr-meth-args-trailing-comma-undefined.js",
        "language/arguments-object/cls-expr-meth-static-args-trailing-comma-multiple.js",
        "language/arguments-object/cls-expr-meth-static-args-trailing-comma-null.js",
        "language/arguments-object/cls-expr-meth-static-args-trailing-comma-single-args.js",
        "language/arguments-object/cls-expr-meth-static-args-trailing-comma-spread-operator.js",
        "language/arguments-object/cls-expr-meth-static-args-trailing-comma-undefined.js",
    };

    [Theory]
    [MemberData(nameof(FunctionEnvironmentLoweringCompilerCases))]
    public void Function_environment_lowering_is_shared_by_function_and_class_paths(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);
}
