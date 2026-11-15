using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitiveYieldStarIteratorTests
{
    public const string PrototypeFactoriesSource = """
        Boolean.prototype[Symbol.iterator] = function* () { yield this.valueOf(); };
        Number.prototype[Symbol.iterator] = function* () {
            yield this.valueOf();
            return "number-end";
        };
        function* booleans() { yield* true; yield* false; }
        function* numbers() { return yield* 7; }
        const booleanIterator = booleans();
        const first = booleanIterator.next();
        console.log(first.value, first.done);
        const second = booleanIterator.next();
        console.log(second.value, second.done);
        const third = booleanIterator.next();
        console.log(third.value === undefined, third.done);
        const numberIterator = numbers();
        const numberFirst = numberIterator.next();
        console.log(numberFirst.value, numberFirst.done);
        const numberLast = numberIterator.next();
        console.log(numberLast.value, numberLast.done);
        """;

    public const string ProtocolSource = """
        let gets = 0;
        let factories = 0;
        let nextReads = 0;
        const marker = {};
        Object.defineProperty(Boolean.prototype, Symbol.iterator, {
            configurable: true,
            get() {
                "use strict";
                gets++;
                console.log("get", typeof this, this === true);
                const primitive = this;
                return function () {
                    "use strict";
                    factories++;
                    console.log("factory", typeof this, this === primitive);
                    let calls = 0;
                    return {
                        get next() {
                            nextReads++;
                            return function (value) {
                                console.log("next", arguments.length, value === undefined);
                                if (calls++ === 0) return { value: "yielded", done: false };
                                return { value: value, done: true };
                            };
                        },
                        return(value) {
                            console.log("close", value === marker);
                            return { value: value, done: true };
                        }
                    };
                };
            }
        });
        function* outer() { return yield* true; }
        const iterator = outer();
        const first = iterator.next("ignored");
        console.log(first.value, first.done);
        const completed = iterator.next(marker);
        console.log(completed.value === marker, completed.done, gets, factories, nextReads);
        const closing = outer();
        closing.next();
        const closed = closing.return(marker);
        console.log(closed.value === marker, closed.done, gets, factories, nextReads);
        """;

    public const string InvalidMethodsSource = """
        function check(prototype, primitive) {
            function* outer() { yield* primitive; }
            for (const method of [null, undefined, false, 0, {}]) {
                prototype[Symbol.iterator] = method;
                const iterator = outer();
                try {
                    iterator.next();
                    console.log("unexpected acceptance");
                } catch (error) {
                    console.log(error instanceof TypeError, iterator.next().done);
                }
            }
            delete prototype[Symbol.iterator];
            const iterator = outer();
            try {
                iterator.next();
                console.log("unexpected acceptance");
            } catch (error) {
                console.log(error instanceof TypeError, iterator.next().done);
            }
        }
        check(Boolean.prototype, true);
        check(Number.prototype, 7);
        """;

    public const string GetterFailureSource = """
        const marker = {};
        let gets = 0;
        Object.defineProperty(Boolean.prototype, Symbol.iterator, {
            get() { gets++; throw marker; }
        });
        function* outer() { yield* false; }
        const iterator = outer();
        try {
            iterator.next();
        } catch (error) {
            console.log(error === marker, gets, iterator.next().done);
        }
        """;

    [Theory, ModeData]
    public void YieldStar_UsesPrimitiveBooleanAndNumberPrototypeFactories(ExecutionMode mode)
    {
        Assert.Equal("true false\nfalse false\ntrue true\n7 false\nnumber-end true\n",
            TestHarness.RunUnchecked(PrototypeFactoriesSource, mode));
    }

    [Theory, ModeData]
    public void YieldStar_PrimitiveAccessorRetainsReceiverAndAcquiredProtocol(ExecutionMode mode)
    {
        Assert.Equal("get boolean true\nfactory boolean true\nnext 1 true\nyielded false\n"
            + "next 1 false\ntrue true 1 1 1\nget boolean true\nfactory boolean true\n"
            + "next 1 true\nclose true\ntrue true 2 2 2\n",
            TestHarness.RunUnchecked(ProtocolSource, mode));
    }

    [Theory, ModeData]
    public void YieldStar_PrimitiveMissingAndNonCallableFactoriesThrowTypeError(ExecutionMode mode)
    {
        Assert.Equal(string.Concat(Enumerable.Repeat("true true\n", 12)),
            TestHarness.RunUnchecked(InvalidMethodsSource, mode));
    }

    [Theory, ModeData]
    public void YieldStar_PrimitiveFactoryGetterFailurePreservesIdentity(ExecutionMode mode)
    {
        Assert.Equal("true 1 true\n", TestHarness.RunUnchecked(GetterFailureSource, mode));
    }
    [Theory, ModeData]
    public void YieldStar_NoniterablePrimitiveReachesLocalCatchAndFinally(ExecutionMode mode)
    {
        const string source = """
            function* outer(value) {
                try { yield* value; }
                catch (error) { console.log(error instanceof TypeError); }
                finally { console.log('finally'); }
            }
            for (const value of [true, 7, null, undefined]) {
                console.log(outer(value).next().done);
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true\nfinally\ntrue\n", 4)),
            TestHarness.RunUnchecked(source, mode));
    }

}
