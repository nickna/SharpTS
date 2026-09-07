using Xunit;

namespace SharpTS.Test262;

public sealed partial class RuntimeConformanceTests
{

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
    [InlineData("built-ins/Promise/resolve/arg-uniq-ctor.js")]
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
    [InlineData("built-ins/Promise/prototype/finally/rejected-observable-then-calls-argument.js")]
    [InlineData("built-ins/Promise/prototype/finally/rejection-reason-no-fulfill.js")]
    [InlineData("built-ins/Promise/prototype/finally/this-value-then-not-callable.js")]
    [InlineData("built-ins/Promise/prototype/finally/this-value-then-poisoned.js")]
    [InlineData("built-ins/Promise/prototype/finally/this-value-then-throws.js")]
    [InlineData("built-ins/Promise/prototype/finally/this-value-thenable.js")]
    [InlineData("built-ins/Promise/prototype/finally/this-value-proxy.js")]
    public void Promise_finally_dynamically_invokes_then(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/allSettled/resolved-then-catch-finally.js")]
    [InlineData("built-ins/Promise/prototype/then/resolve-pending-rejected-thenable.js")]
    [InlineData("built-ins/Promise/prototype/then/resolve-settled-rejected-thenable.js")]
    [InlineData("built-ins/Promise/resolve/resolve-prms-cstm-then.js")]
    public void Promise_rejection_handlers_adopt_returned_promises(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/prototype/then/ctor-access-count.js")]
    [InlineData("built-ins/Promise/prototype/then/rxn-handler-fulfilled-invoke-strict.js")]
    [InlineData("built-ins/Promise/prototype/then/rxn-handler-rejected-invoke-strict.js")]
    public void Promise_then_observes_species_and_strict_reaction_calls(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/resolve-thenable-immed.js")]
    [InlineData("built-ins/Promise/resolve-thenable-deferred.js")]
    public void Promise_executor_resolve_adopts_promises(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/exception-after-resolve-in-executor.js")]
    [InlineData("built-ins/Promise/exception-after-resolve-in-thenable-job.js")]
    [InlineData("built-ins/Promise/prototype/then/resolve-settled-rejected-prms-cstm-then.js")]
    [InlineData("built-ins/Promise/race/resolve-poisoned-then.js")]
    [InlineData("built-ins/Promise/race/resolve-thenable.js")]
    [InlineData("built-ins/Promise/resolve-self.js")]
    [InlineData("built-ins/Promise/resolve/S25.4.4.5_A4.1_T1.js")]
    [InlineData("built-ins/Promise/executor-function-not-a-constructor.js")]
    public void Promise_executor_resolve_adopts_thenables(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/allSettled/call-resolve-element-after-return.js")]
    [InlineData("built-ins/Promise/allSettled/call-resolve-element-items.js")]
    [InlineData("built-ins/Promise/allSettled/call-resolve-element.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-before-loop-exit.js")]
    [InlineData("built-ins/Promise/any/call-reject-element-after-return.js")]
    public void Promise_custom_capabilities_settle_synchronously(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/any/capability-resolve-throws-reject.js")]
    [InlineData("built-ins/Promise/reject/capability-invocation-error.js")]
    [InlineData("built-ins/Promise/reject/capability-invocation.js")]
    [InlineData("built-ins/Promise/resolve/capability-invocation-error.js")]
    public void Promise_custom_capability_invocations_are_observed(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/reject-deferred.js")]
    [InlineData("built-ins/Promise/all/reject-immed.js")]
    [InlineData("built-ins/Promise/all/resolve-ignores-late-rejection-deferred.js")]
    [InlineData("built-ins/Promise/all/resolve-ignores-late-rejection.js")]
    [InlineData("built-ins/Promise/allSettled/reject-deferred.js")]
    [InlineData("built-ins/Promise/allSettled/reject-ignored-deferred.js")]
    [InlineData("built-ins/Promise/allSettled/reject-ignored-immed.js")]
    [InlineData("built-ins/Promise/allSettled/reject-immed.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-ignores-late-rejection-deferred.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-ignores-late-rejection.js")]
    [InlineData("built-ins/Promise/any/invoke-then-on-promises-every-iteration.js")]
    [InlineData("built-ins/Promise/any/reject-deferred.js")]
    [InlineData("built-ins/Promise/any/reject-ignored-deferred.js")]
    [InlineData("built-ins/Promise/any/reject-immed.js")]
    [InlineData("built-ins/Promise/any/resolve-ignores-late-rejection-deferred.js")]
    [InlineData("built-ins/Promise/any/resolve-ignores-late-rejection.js")]
    [InlineData("built-ins/Promise/promise.js")]
    [InlineData("built-ins/Promise/prototype/finally/rejection-reason-override-with-throw.js")]
    [InlineData("built-ins/Promise/prototype/finally/resolved-observable-then-calls.js")]
    [InlineData("built-ins/Promise/race/reject-deferred.js")]
    [InlineData("built-ins/Promise/race/reject-immed.js")]
    [InlineData("built-ins/Promise/race/resolve-ignores-late-rejection-deferred.js")]
    [InlineData("built-ins/Promise/race/resolve-ignores-late-rejection.js")]
    [InlineData("built-ins/Promise/race/resolve-non-obj.js")]
    [InlineData("built-ins/Promise/race/resolve-non-thenable.js")]
    public void Promise_combinators_and_finally_preserve_settlement_semantics(
        string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/prototype/finally/rejection-reason-override-with-throw.js")]
    [InlineData("built-ins/Promise/prototype/finally/resolved-observable-then-calls.js")]
    public void Compiled_Promise_finally_preserves_completion_semantics(string relativePath)
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
    [InlineData("built-ins/Promise/all/S25.4.4.1_A4.1_T1.js")]
    [InlineData("built-ins/Promise/all/capability-executor-not-callable.js")]
    [InlineData("built-ins/Promise/allSettled/capability-executor-not-callable.js")]
    [InlineData("built-ins/Promise/any/capability-executor-not-callable.js")]
    [InlineData("built-ins/Promise/race/S25.4.4.3_A3.1_T1.js")]
    [InlineData("built-ins/Promise/race/S25.4.4.3_A3.1_T2.js")]
    [InlineData("built-ins/Promise/race/capability-executor-not-callable.js")]
    [InlineData("built-ins/Promise/reject/S25.4.4.4_A3.1_T1.js")]
    [InlineData("built-ins/Promise/reject/capability-executor-not-callable.js")]
    [InlineData("built-ins/Promise/resolve/capability-executor-not-callable.js")]
    public void Promise_custom_capabilities_require_callable_callbacks(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/capability-executor-called-twice.js")]
    [InlineData("built-ins/Promise/allSettled/capability-executor-called-twice.js")]
    [InlineData("built-ins/Promise/any/capability-executor-called-twice.js")]
    [InlineData("built-ins/Promise/race/capability-executor-called-twice.js")]
    [InlineData("built-ins/Promise/reject/capability-executor-called-twice.js")]
    [InlineData("built-ins/Promise/resolve/capability-executor-called-twice.js")]
    public void Promise_capability_executor_rejects_repeated_non_undefined_slots(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/executor-call-context-strict.js")]
    public void Promise_jobs_invoke_guest_functions_with_undefined_this(string relativePath)
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
    [InlineData("built-ins/Promise/all/invoke-resolve-on-promises-every-iteration-of-custom.js")]
    [InlineData("built-ins/Promise/all/iter-step-err-reject.js")]
    [InlineData("built-ins/Promise/all/resolve-throws-iterator-return-is-not-callable.js")]
[InlineData("built-ins/Promise/all/resolve-throws-iterator-return-null-or-undefined.js")]
[InlineData("built-ins/Promise/allSettled/iter-next-val-err-reject.js")]
[InlineData("built-ins/Promise/any/iter-returns-false-reject.js")]
public void Promise_combinators_share_iterator_and_resolution_semantics(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/resolve/S25.4.4.5_A3.1_T1.js")]
    [InlineData("built-ins/Promise/resolve/S25.Promise_resolve_foreign_thenable_1.js")]
    [InlineData("built-ins/Promise/resolve/S25.Promise_resolve_foreign_thenable_2.js")]
    [InlineData("built-ins/Promise/resolve/arg-poisoned-then.js")]
    [InlineData("built-ins/Promise/resolve/resolve-from-promise-capability.js")]
    [InlineData("built-ins/Promise/resolve/resolve-poisoned-then.js")]
    [InlineData("built-ins/Promise/resolve/resolve-self.js")]
    [InlineData("built-ins/Promise/resolve/resolve-thenable.js")]
    public void Promise_resolve_uses_capabilities_and_queued_thenable_jobs(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/prototype/finally/rejected-observable-then-calls.js")]
    [InlineData("built-ins/Promise/prototype/then/resolve-settled-fulfilled-prms-cstm-then.js")]
    [InlineData("built-ins/Promise/prototype/then/resolve-pending-fulfilled-poisoned-then.js")]
    [InlineData("built-ins/Promise/prototype/then/resolve-pending-fulfilled-prms-cstm-then.js")]
    [InlineData("built-ins/Promise/prototype/then/resolve-pending-rejected-poisoned-then.js")]
    [InlineData("built-ins/Promise/prototype/then/resolve-pending-rejected-prms-cstm-then.js")]
    public void Promise_reactions_adopt_observable_thenables(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/invoke-resolve-get-error-reject.js")]
    [InlineData("built-ins/Promise/all/invoke-resolve-get-error.js")]
    [InlineData("built-ins/Promise/allSettled/invoke-resolve-get-error-reject.js")]
    [InlineData("built-ins/Promise/allSettled/invoke-resolve-get-error.js")]
    [InlineData("built-ins/Promise/any/invoke-resolve-get-error-reject.js")]
    [InlineData("built-ins/Promise/any/invoke-resolve-get-error.js")]
    [InlineData("built-ins/Promise/any/invoke-resolve-get-once-multiple-calls.js")]
    [InlineData("built-ins/Promise/any/invoke-resolve-get-once-no-calls.js")]
    [InlineData("built-ins/Promise/any/invoke-then.js")]
    [InlineData("built-ins/Promise/race/invoke-resolve-get-error-reject.js")]
    [InlineData("built-ins/Promise/race/invoke-resolve-get-error.js")]
    public void Promise_combinators_use_constructor_capability_and_observable_then(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/resolve-element-function-length.js")]
    [InlineData("built-ins/Promise/all/resolve-element-function-name.js")]
    [InlineData("built-ins/Promise/all/resolve-element-function-property-order.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-element-function-length.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-element-function-name.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-element-function-property-order.js")]
    [InlineData("built-ins/Promise/allSettled/reject-element-function-length.js")]
    [InlineData("built-ins/Promise/allSettled/reject-element-function-name.js")]
    [InlineData("built-ins/Promise/allSettled/reject-element-function-property-order.js")]
    [InlineData("built-ins/Promise/any/reject-element-function-length.js")]
    [InlineData("built-ins/Promise/any/reject-element-function-name.js")]
    [InlineData("built-ins/Promise/any/reject-element-function-property-order.js")]
    public void Promise_combinator_callbacks_expose_builtin_metadata(string relativePath)
        => AssertPassInBothModes(relativePath);

    public static TheoryData<string> ExposedPromiseInterpreterDeficitCases => new()
    {
        "built-ins/Promise/all/capability-resolve-throws-reject.js",
        "built-ins/Promise/all/invoke-resolve-get-error-reject.js",
        "built-ins/Promise/all/invoke-resolve-get-error.js",
        "built-ins/Promise/all/invoke-resolve-get-once-no-calls.js",
        "built-ins/Promise/all/invoke-then-get-error-reject.js",
        "built-ins/Promise/all/invoke-then.js",
        "built-ins/Promise/all/resolve-element-function-length.js",
        "built-ins/Promise/all/resolve-element-function-name.js",
        "built-ins/Promise/all/resolve-element-function-property-order.js",
        "built-ins/Promise/allSettled/capability-resolve-throws-reject.js",
        "built-ins/Promise/allSettled/invoke-resolve-get-error-reject.js",
        "built-ins/Promise/allSettled/invoke-resolve-get-error.js",
        "built-ins/Promise/allSettled/invoke-resolve-get-once-multiple-calls.js",
        "built-ins/Promise/allSettled/invoke-resolve-get-once-no-calls.js",
        "built-ins/Promise/allSettled/invoke-then.js",
        "built-ins/Promise/allSettled/reject-element-function-length.js",
        "built-ins/Promise/allSettled/reject-element-function-multiple-calls.js",
        "built-ins/Promise/allSettled/reject-element-function-name.js",
        "built-ins/Promise/allSettled/reject-element-function-property-order.js",
        "built-ins/Promise/allSettled/resolve-element-function-length.js",
        "built-ins/Promise/allSettled/resolve-element-function-name.js",
        "built-ins/Promise/allSettled/resolve-element-function-property-order.js",
        "built-ins/Promise/any/invoke-resolve-error-close.js",
        "built-ins/Promise/any/invoke-resolve-get-error-reject.js",
        "built-ins/Promise/any/invoke-resolve-get-error.js",
        "built-ins/Promise/any/invoke-resolve-get-once-multiple-calls.js",
        "built-ins/Promise/any/invoke-resolve-get-once-no-calls.js",
        "built-ins/Promise/any/invoke-then.js",
        "built-ins/Promise/any/iter-next-val-err-no-close.js",
        "built-ins/Promise/any/iter-step-err-no-close.js",
        "built-ins/Promise/any/reject-element-function-length.js",
        "built-ins/Promise/any/reject-element-function-name.js",
        "built-ins/Promise/any/reject-element-function-property-order.js",
        "built-ins/Promise/exec-args.js",
        "built-ins/Promise/race/invoke-resolve-get-error-reject.js",
        "built-ins/Promise/race/invoke-resolve-get-error.js",
        "built-ins/Promise/race/invoke-resolve-get-once-multiple-calls.js",
        "built-ins/Promise/race/invoke-resolve-get-once-no-calls.js",
        "built-ins/Promise/race/invoke-then-get-error-reject.js",
        "built-ins/Promise/race/invoke-then.js",
        "built-ins/Promise/race/reject-from-same-thenable.js",
        "built-ins/Promise/race/resolve-from-same-thenable.js",
        "built-ins/Promise/reject-function-length.js",
        "built-ins/Promise/reject-function-name.js",
        "built-ins/Promise/reject-function-property-order.js",
        "built-ins/Promise/resolve-function-length.js",
        "built-ins/Promise/resolve-function-name.js",
        "built-ins/Promise/resolve-function-property-order.js",
    };

    [Theory]
    [MemberData(nameof(ExposedPromiseInterpreterDeficitCases))]
    public void Exposed_Promise_interpreter_deficits_pass_in_both_modes(string relativePath)
        => AssertPassInBothModes(relativePath);

    [Theory]
    [InlineData("built-ins/Promise/all/call-resolve-element-after-return.js")]
    [InlineData("built-ins/Promise/all/call-resolve-element-items.js")]
    [InlineData("built-ins/Promise/all/call-resolve-element.js")]
    [InlineData("built-ins/Promise/all/capability-resolve-throws-no-close.js")]
    [InlineData("built-ins/Promise/all/resolve-before-loop-exit-from-same.js")]
    [InlineData("built-ins/Promise/all/resolve-before-loop-exit.js")]
    [InlineData("built-ins/Promise/all/resolve-from-same-thenable.js")]
    [InlineData("built-ins/Promise/allKeyed/arg-not-object-reject.js")]
    [InlineData("built-ins/Promise/allSettled/capability-resolve-throws-no-close.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-before-loop-exit-from-same.js")]
    [InlineData("built-ins/Promise/allSettled/resolve-from-same-thenable.js")]
    [InlineData("built-ins/Promise/allSettledKeyed/arg-not-object-reject.js")]
    [InlineData("built-ins/Promise/any/call-reject-element-items.js")]
    [InlineData("built-ins/Promise/any/reject-from-same-thenable.js")]
    [InlineData("built-ins/Promise/any/resolve-before-loop-exit.js")]
    [InlineData("built-ins/Promise/race/resolve-throws-iterator-return-is-not-callable.js")]
    [InlineData("built-ins/Promise/race/resolve-throws-iterator-return-null-or-undefined.js")]
    [InlineData("built-ins/Promise/resolve-poisoned-then-deferred.js")]
    [InlineData("built-ins/Promise/resolve-poisoned-then-immed.js")]
    public void Remaining_Promise_compiler_parity(string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);

    public static TheoryData<string> PromiseCombinatorCompilerParityCases => new()
    {
        "built-ins/Promise/all/invoke-resolve-error-close.js",
        "built-ins/Promise/all/invoke-resolve-get-once-multiple-calls.js",
        "built-ins/Promise/all/invoke-then-error-close.js",
        "built-ins/Promise/all/invoke-then-get-error-close.js",
        "built-ins/Promise/all/iter-next-val-err-no-close.js",
        "built-ins/Promise/all/iter-step-err-no-close.js",
        "built-ins/Promise/all/resolve-poisoned-then.js",
        "built-ins/Promise/all/resolve-thenable.js",
        "built-ins/Promise/allSettled/invoke-resolve-error-close.js",
        "built-ins/Promise/allSettled/invoke-then-error-close.js",
        "built-ins/Promise/allSettled/invoke-then-error-reject.js",
        "built-ins/Promise/allSettled/invoke-then-get-error-close.js",
        "built-ins/Promise/allSettled/invoke-then-get-error-reject.js",
        "built-ins/Promise/allSettled/iter-next-val-err-no-close.js",
        "built-ins/Promise/allSettled/iter-step-err-no-close.js",
        "built-ins/Promise/allSettled/resolve-poisoned-then.js",
        "built-ins/Promise/allSettled/resolve-thenable.js",
        "built-ins/Promise/any/invoke-then-error-close.js",
        "built-ins/Promise/any/invoke-then-error-reject.js",
        "built-ins/Promise/any/invoke-then-get-error-close.js",
        "built-ins/Promise/any/invoke-then-get-error-reject.js",
        "built-ins/Promise/any/resolve-before-loop-exit-from-same.js",
        "built-ins/Promise/any/resolve-from-same-thenable.js",
        "built-ins/Promise/race/invoke-resolve-error-close.js",
        "built-ins/Promise/race/invoke-then-error-close.js",
        "built-ins/Promise/race/invoke-then-get-error-close.js",
        "built-ins/Promise/race/iter-next-val-err-no-close.js",
        "built-ins/Promise/race/iter-step-err-no-close.js",
        "built-ins/Promise/race/resolve-self.js",
    };

    [Theory]
    [MemberData(nameof(PromiseCombinatorCompilerParityCases))]
    public void Promise_combinator_iterator_close_and_error_paths_match_in_compiled_mode(
        string relativePath)
        => AssertPass(relativePath, Test262ExecutionMode.Compiled);
}
