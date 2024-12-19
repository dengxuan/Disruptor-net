using System;
using System.Diagnostics;
using NUnit.Framework;

namespace Disruptor.Tests;

public class PhasedBackoffWaitStrategyTestWithLock : WaitStrategyFixture<PhasedBackoffWaitStrategy>
{
    private static readonly double _stopwatchTickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

    protected override PhasedBackoffWaitStrategy CreateWaitStrategy()
    {
        return PhasedBackoffWaitStrategy.WithLock(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public void ShouldComputeStopwatchTicksFromTimeSpan()
    {
        var spinTimeout = TimeSpan.FromTicks(2_000);
        var yieldTimeout = TimeSpan.FromMilliseconds(15);
        var waitStrategy = PhasedBackoffWaitStrategy.WithLock(spinTimeout, yieldTimeout);

        Assert.That(GetElapsedTime(waitStrategy.SpinTimeout), Is.EqualTo(spinTimeout));
        Assert.That(GetElapsedTime(waitStrategy.YieldTimeout), Is.EqualTo(yieldTimeout));
    }

    private static TimeSpan GetElapsedTime(long stopwatchTicks) => new((long)(stopwatchTicks * _stopwatchTickFrequency));
}
