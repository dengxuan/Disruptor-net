using Disruptor.Tests.Support;
using NUnit.Framework;

namespace Disruptor.Tests;

[TestFixture]
public class AggregateEventHandlerTests
{
    private readonly DummyEventHandler<int[]> _eh1;
    private readonly DummyEventHandler<int[]> _eh2;
    private readonly DummyEventHandler<int[]> _eh3;

    public AggregateEventHandlerTests()
    {
        _eh1 = new DummyEventHandler<int[]>();
        _eh2 = new DummyEventHandler<int[]>();
        _eh3 = new DummyEventHandler<int[]>();
    }

    [Test]
    public void ShouldCallOnEventInSequence()
    {
        var evt = new[] { 7 };
        const long sequence = 3L;
        const bool endOfBatch = true;

        var aggregateEventHandler = new AggregateEventHandler<int[]>(_eh1, _eh2, _eh3);

        aggregateEventHandler.OnEvent(evt, sequence, endOfBatch);

        AssertLastEvent(evt, sequence, _eh1, _eh2, _eh3);
    }

    [Test]
    public void ShouldCallOnStartInSequence()
    {
        var aggregateEventHandler = new AggregateEventHandler<int[]>(_eh1, _eh2, _eh3);

        aggregateEventHandler.OnStart();

        AssertStartCalls(1, _eh1, _eh2, _eh3);
    }

    [Test]
    public void ShouldCallOnShutdownInSequence()
    {
        var aggregateEventHandler = new AggregateEventHandler<int[]>(_eh1, _eh2, _eh3); ;

        aggregateEventHandler.OnShutdown();

        AssertShutdownCalls(1, _eh1, _eh2, _eh3);
    }

    [Test]
    public void ShouldHandleEmptyListOfEventHandlers()
    {
        var aggregateEventHandler = new AggregateEventHandler<int[]>();

        aggregateEventHandler.OnEvent(new[] { 7 }, 0L, true);
        aggregateEventHandler.OnStart();
        aggregateEventHandler.OnShutdown();
    }

    private static void AssertLastEvent(int[] evt, long sequence, params DummyEventHandler<int[]>[] handlers)
    {
        foreach (var handler in handlers)
        {
            Assert.That(evt, Is.SameAs(handler.LastEvent));
            Assert.That(sequence, Is.EqualTo(handler.LastSequence));
        }
    }

    private static void AssertStartCalls(int startCalls, params DummyEventHandler<int[]>[] handlers)
    {
        foreach (var handler in handlers)
        {
            Assert.That(startCalls, Is.EqualTo(handler.StartCalls));
        }
    }

    private static void AssertShutdownCalls(int shutdownCalls, params DummyEventHandler<int[]>[] handlers)
    {
        foreach (var handler in handlers)
        {
            Assert.That(shutdownCalls, Is.EqualTo(handler.ShutdownCalls));
        }
    }
}
