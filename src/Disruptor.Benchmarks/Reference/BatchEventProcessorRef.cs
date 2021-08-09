using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Disruptor.Benchmarks.Reference
{
    public class BatchEventProcessorRef<T, TDataProvider, TSequenceBarrier, TEventHandler, TBatchStartAware> : IBatchEventProcessor<T>
        where T : class
        where TDataProvider : IDataProvider<T>
        where TSequenceBarrier : ISequenceBarrier
        where TEventHandler : IEventHandler<T>
        where TBatchStartAware : IBatchStartAware
    {
        // ReSharper disable FieldCanBeMadeReadOnly.Local (performance: the runtime type will be a struct)
        private TDataProvider _dataProvider;
        private TSequenceBarrier _sequenceBarrier;
        private TEventHandler _eventHandler;
        private TBatchStartAware _batchStartAware;
        // ReSharper restore FieldCanBeMadeReadOnly.Local

        private readonly Sequence _sequence = new Sequence();
        private readonly ITimeoutHandler _timeoutHandler;
        private readonly ManualResetEventSlim _started = new ManualResetEventSlim();
        private IExceptionHandler<T> _exceptionHandler = new FatalExceptionHandler();
        private volatile int _runState = ProcessorRunStates.Idle;

        /// <summary>
        /// Construct a BatchEventProcessor that will automatically track the progress by updating its sequence when
        /// the <see cref="IEventHandler{T}.OnEvent"/> method returns.
        ///
        /// Consider using <see cref="BatchEventProcessorFactory"/> to create your <see cref="IEventProcessor"/>.
        /// </summary>
        /// <param name="dataProvider">dataProvider to which events are published</param>
        /// <param name="sequenceBarrier">SequenceBarrier on which it is waiting.</param>
        /// <param name="eventHandler">eventHandler is the delegate to which events are dispatched.</param>
        /// <param name="batchStartAware"></param>
        public BatchEventProcessorRef(TDataProvider dataProvider, TSequenceBarrier sequenceBarrier, TEventHandler eventHandler, TBatchStartAware batchStartAware)
        {
            _dataProvider = dataProvider;
            _sequenceBarrier = sequenceBarrier;
            _eventHandler = eventHandler;
            _batchStartAware = batchStartAware;

            if (eventHandler is IEventProcessorSequenceAware sequenceAware)
                sequenceAware.SetSequenceCallback(_sequence);

            _timeoutHandler = eventHandler as ITimeoutHandler;
        }

        /// <summary>
        /// <see cref="IEventProcessor.Sequence"/>
        /// </summary>
        public ISequence Sequence => _sequence;

        /// <summary>
        /// Signal that this <see cref="IEventProcessor"/> should stop when it has finished consuming at the next clean break.
        /// It will call <see cref="ISequenceBarrier.Alert"/> to notify the thread to check status.
        /// </summary>
        public void Halt()
        {
            _runState = ProcessorRunStates.Halted;
            _sequenceBarrier.Alert();
        }

        /// <summary>
        /// <see cref="IEventProcessor.IsRunning"/>
        /// </summary>
        public bool IsRunning => _runState != ProcessorRunStates.Idle;

        /// <summary>
        /// Set a new <see cref="IExceptionHandler{T}"/> for handling exceptions propagated out of the <see cref="IEventHandler{T}"/>
        /// </summary>
        /// <param name="exceptionHandler">exceptionHandler to replace the existing exceptionHandler.</param>
        public void SetExceptionHandler(IExceptionHandler<T> exceptionHandler)
        {
            _exceptionHandler = exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));
        }

        /// <summary>
        /// Waits before the event processor enters the <see cref="IsRunning"/> state.
        /// </summary>
        /// <param name="timeout">maximum wait duration</param>
        public void WaitUntilStarted(TimeSpan timeout)
        {
            _started.Wait(timeout);
        }

        /// <summary>
        /// It is ok to have another thread rerun this method after a halt().
        /// </summary>
        /// <exception cref="InvalidOperationException">if this object instance is already running in a thread</exception>
        public void Run()
        {
#pragma warning disable 420
            var previousRunning = Interlocked.CompareExchange(ref _runState, ProcessorRunStates.Running, ProcessorRunStates.Idle);
#pragma warning restore 420

            if (previousRunning == ProcessorRunStates.Running)
            {
                throw new InvalidOperationException("Thread is already running");
            }

            if (previousRunning == ProcessorRunStates.Idle)
            {
                _sequenceBarrier.ClearAlert();

                NotifyStart();
                try
                {
                    if (_runState == ProcessorRunStates.Running)
                    {
                        ProcessEvents();
                    }
                }
                finally
                {
                    NotifyShutdown();
                    _runState = ProcessorRunStates.Idle;
                }
            }
            else
            {
                EarlyExit();
            }
        }

        [MethodImpl(Constants.AggressiveOptimization)]
        private void ProcessEvents()
        {
            T evt = null;
            var nextSequence = _sequence.Value + 1L;

            while (true)
            {
                try
                {
                    var availableSequence = _sequenceBarrier.WaitFor(nextSequence);

                    // WaitFor can return a value lower than nextSequence, for example when using a MultiProducerSequencer.
                    // The Java version includes the test "if (availableSequence >= nextSequence)" to avoid invoking OnBatchStart on empty batches.
                    // However, this test has a negative impact on performance even for event handlers that are not IBatchStartAware.
                    // This is unfortunate because this test should be removed by the JIT when OnBatchStart is a noop.
                    // => The test is currently implemented on struct proxies. See BatchEventProcessor<T>.BatchStartAware and StructProxy.
                    // For some reason this also improves BatchEventProcessor performance for IBatchStartAware event handlers.

                    _batchStartAware.OnBatchStart(availableSequence - nextSequence + 1);

                    while (nextSequence <= availableSequence)
                    {
                        evt = _dataProvider[nextSequence];
                        _eventHandler.OnEvent(evt, nextSequence, nextSequence == availableSequence);
                        nextSequence++;
                    }

                    _sequence.SetValue(availableSequence);
                }
                catch (TimeoutException)
                {
                    NotifyTimeout(_sequence.Value);
                }
                catch (AlertException)
                {
                    if (_runState != ProcessorRunStates.Running)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _exceptionHandler.HandleEventException(ex, nextSequence, evt);
                    _sequence.SetValue(nextSequence);
                    nextSequence++;
                }
            }
        }

        private void EarlyExit()
        {
            NotifyStart();
            NotifyShutdown();
        }

        private void NotifyTimeout(long availableSequence)
        {
            try
            {
                _timeoutHandler?.OnTimeout(availableSequence);
            }
            catch (Exception ex)
            {
                _exceptionHandler.HandleEventException(ex, availableSequence, null);
            }
        }

        /// <summary>
        /// Notifies the EventHandler when this processor is starting up
        /// </summary>
        private void NotifyStart()
        {
            var lifecycleAware = _eventHandler as ILifecycleAware;
            if (lifecycleAware != null)
            {
                try
                {
                    lifecycleAware.OnStart();
                }
                catch (Exception e)
                {
                    _exceptionHandler.HandleOnStartException(e);
                }
            }

            _started.Set();
        }

        /// <summary>
        /// Notifies the EventHandler immediately prior to this processor shutting down
        /// </summary>
        private void NotifyShutdown()
        {
            var lifecycleAware = _eventHandler as ILifecycleAware;
            if (lifecycleAware != null)
            {
                try
                {
                    lifecycleAware.OnShutdown();
                }
                catch (Exception e)
                {
                    _exceptionHandler.HandleOnShutdownException(e);
                }
            }

            _started.Reset();
        }
    }
}
