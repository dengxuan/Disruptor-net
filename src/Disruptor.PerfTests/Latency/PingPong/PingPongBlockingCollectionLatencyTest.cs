﻿using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Disruptor.PerfTests.Support;
using Disruptor.Util;
using HdrHistogram;

namespace Disruptor.PerfTests.Latency.PingPong;

public class PingPongBlockingCollectionLatencyTest : ILatencyTest, IExternalTest
{
    private const int _bufferSize = 1024;
    private const long _iterations = 100 * 1000 * 30;
    private const long _pauseDurationInNano = 1000;

    private readonly BlockingCollection<long> _pingQueue = new(_bufferSize);
    private readonly BlockingCollection<long> _pongQueue = new(_bufferSize);
    private readonly QueuePinger _pinger;
    private readonly QueuePonger _ponger;

    public PingPongBlockingCollectionLatencyTest(ProgramOptions options)
    {
        _pinger = new QueuePinger(_pingQueue, _pongQueue, options.GetCustomCpu(0));
        _ponger = new QueuePonger(_pingQueue, _pongQueue, options.GetCustomCpu(1));
    }

    public void Run(LatencySessionContext sessionContext)
    {
        var cancellationToken = new CancellationTokenSource();
        var signal = new ManualResetEvent(false);
        var globalSignal = new CountdownEvent(3);
        _pinger.Reset(globalSignal, signal, sessionContext.Histogram);
        _ponger.Reset(globalSignal, cancellationToken.Token);

        _pinger.Start();
        _ponger.Start();

        globalSignal.Signal();
        globalSignal.Wait();
        sessionContext.Start();
        signal.WaitOne();
        sessionContext.Stop();

        cancellationToken.Cancel();
    }

    public int RequiredProcessorCount => 2;

    private class QueuePinger
    {
        private readonly BlockingCollection<long> _pingQueue;
        private readonly BlockingCollection<long> _pongQueue;
        private readonly int? _cpu;
        private readonly double _pauseDurationInStopwatchTicks;

        private HistogramBase _histogram;
        private ManualResetEvent _signal;
        private CountdownEvent _globalSignal;

        public QueuePinger(BlockingCollection<long> pingQueue, BlockingCollection<long> pongQueue, int? cpu)
        {
            _pingQueue = pingQueue;
            _pongQueue = pongQueue;
            _cpu = cpu;
            _pauseDurationInStopwatchTicks = StopwatchUtil.GetTimestampFromNanoseconds(_pauseDurationInNano);
        }

        public void Run()
        {
            using var _ = ThreadAffinityUtil.SetThreadAffinity(_cpu);

            _globalSignal.Signal();
            _globalSignal.Wait();

            Thread.Sleep(1000);
            long counter = 0;

            while (counter < _iterations)
            {
                var t0 = Stopwatch.GetTimestamp();
                _pingQueue.Add(1L);
                counter += _pongQueue.Take();
                var t1 = Stopwatch.GetTimestamp();

                _histogram.RecordValueWithExpectedInterval(StopwatchUtil.ToNanoseconds(t1 - t0), _pauseDurationInNano);

                while (_pauseDurationInStopwatchTicks > (Stopwatch.GetTimestamp() - t1))
                {
                    Thread.Sleep(0);
                }
            }

            _signal.Set();
        }

        public void Reset(CountdownEvent globalSignal, ManualResetEvent signal, HistogramBase histogram)
        {
            _globalSignal = globalSignal;
            _signal = signal;
            _histogram = histogram;
        }

        public void Start()
        {
            PerfTestUtil.StartLongRunning(Run);
        }
    }

    public class QueuePonger
    {
        private readonly BlockingCollection<long> _pingQueue;
        private readonly BlockingCollection<long> _pongQueue;
        private readonly int? _cpu;
        private CancellationToken _cancellationToken;
        private CountdownEvent _globalSignal;

        public QueuePonger(BlockingCollection<long> pingQueue, BlockingCollection<long> pongQueue, int? cpu)
        {
            _pingQueue = pingQueue;
            _pongQueue = pongQueue;
            _cpu = cpu;
        }

        public void Run()
        {
            using var _ = ThreadAffinityUtil.SetThreadAffinity(_cpu);

            _globalSignal.Signal();
            _globalSignal.Wait();

            while (!_cancellationToken.IsCancellationRequested)
            {
                var value = _pingQueue.Take();
                _pongQueue.Add(value);
            }
        }

        public void Reset(CountdownEvent globalSignal, CancellationToken cancellationToken)
        {
            _globalSignal = globalSignal;
            _cancellationToken = cancellationToken;
        }

        public void Start()
        {
            PerfTestUtil.StartLongRunning(Run);
        }
    }
}
