using System;

namespace CML.Simulation
{
    /// <summary>
    /// Converts presentation elapsed time into an uncapped 20 Hz authoritative
    /// clock. Successful calls consume every whole tick and leave only a sub-tick
    /// remainder; a failed tick remains debt and is never skipped.
    /// </summary>
    public sealed class FixedStepSimulationClock
    {
        public const long TimeSpanTicksPerSimulationTick =
            TimeSpan.TicksPerSecond / CML.Foundation.SimulationTick.TicksPerSecond;

        private long _pendingTimeSpanTicks;

        public long PendingTimeSpanTicks => _pendingTimeSpanTicks;

        public long WholeTickDebt => _pendingTimeSpanTicks / TimeSpanTicksPerSimulationTick;

        public FixedStepAdvanceResult Advance(TimeSpan activeElapsed, SimulationEngine engine)
        {
            return Advance(activeElapsed, engine, null);
        }

        public FixedStepAdvanceResult Advance(
            TimeSpan activeElapsed,
            SimulationEngine engine,
            Action beforeEachTick)
        {
            return Advance(activeElapsed, engine, beforeEachTick, null);
        }

        public FixedStepAdvanceResult Advance(
            TimeSpan activeElapsed,
            SimulationEngine engine,
            Action beforeEachTick,
            Action afterEachCommittedTick)
        {
            if (activeElapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(activeElapsed), "Elapsed time cannot be negative.");
            }

            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            _pendingTimeSpanTicks = checked(_pendingTimeSpanTicks + activeElapsed.Ticks);
            ulong committedTicks = 0UL;
            SimulationTickResult? failure = null;

            while (_pendingTimeSpanTicks >= TimeSpanTicksPerSimulationTick)
            {
                beforeEachTick?.Invoke();
                var tickResult = engine.AdvanceOneTick();
                if (!tickResult.Committed)
                {
                    failure = tickResult;
                    break;
                }

                afterEachCommittedTick?.Invoke();
                _pendingTimeSpanTicks -= TimeSpanTicksPerSimulationTick;
                committedTicks = checked(committedTicks + 1UL);
            }

            return new FixedStepAdvanceResult(committedTicks, failure);
        }
    }

    public readonly struct FixedStepAdvanceResult
    {
        public FixedStepAdvanceResult(ulong committedTicks, SimulationTickResult? failure)
        {
            CommittedTicks = committedTicks;
            Failure = failure;
        }

        public ulong CommittedTicks { get; }

        public SimulationTickResult? Failure { get; }

        public bool Succeeded => !Failure.HasValue;
    }
}
