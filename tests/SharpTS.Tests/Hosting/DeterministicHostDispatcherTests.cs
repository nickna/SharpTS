using System.Diagnostics;
using Xunit;

namespace SharpTS.Tests.Hosting;

public sealed class DeterministicHostDispatcherTests
{
    [Fact]
    public void ExternalCompletionDoesNotSpendTheTurnBudgetOnEmptyPolls()
    {
        var dispatcher = new DeterministicHostDispatcher();
        bool completed = false;
        var producer = new Thread(() =>
        {
            Thread.Sleep(50);
            dispatcher.Post(() => completed = true);
        }) { IsBackground = true };
        producer.Start();
        try
        {
            dispatcher.RunUntil(() => completed, maximumTurns: 1, timeout: TimeSpan.FromSeconds(2));
            Assert.True(completed);
        }
        finally { producer.Join(TimeSpan.FromSeconds(2)); }
    }

    [Fact]
    public void MissingCompletionHasAWallClockDeadline()
    {
        var dispatcher = new DeterministicHostDispatcher();
        var clock = Stopwatch.StartNew();
        var error = Assert.Throws<TimeoutException>(() =>
            dispatcher.RunUntil(() => false, timeout: TimeSpan.FromMilliseconds(50)));
        Assert.Contains("waiting for work", error.Message);
        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ReplenishingQueueStillHasATurnLimit()
    {
        var dispatcher = new DeterministicHostDispatcher();
        int calls = 0;
        void Repost() { calls++; dispatcher.Post(Repost); }
        dispatcher.Post(Repost);
        var error = Assert.Throws<TimeoutException>(() => dispatcher.RunUntil(() => false, maximumTurns: 3));
        Assert.Contains("3 turns", error.Message);
        Assert.Equal(3, calls);
    }
}
