using System;

namespace CML.Simulation
{
    /// <summary>
    /// Test/integration helper that represents 1/fps seconds exactly over a full
    /// second by carrying TimeSpan's sub-frame remainder between frames.
    /// </summary>
    public sealed class ExactFramePacer
    {
        private readonly uint _framesPerSecond;
        private ulong _remainder;

        public ExactFramePacer(uint framesPerSecond)
        {
            if (framesPerSecond == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
            }

            _framesPerSecond = framesPerSecond;
        }

        public TimeSpan NextFrameDuration()
        {
            var numerator = checked(_remainder + (ulong)TimeSpan.TicksPerSecond);
            var elapsedTicks = numerator / _framesPerSecond;
            _remainder = numerator % _framesPerSecond;
            if (elapsedTicks > long.MaxValue)
            {
                throw new OverflowException("Frame duration is outside the TimeSpan range.");
            }

            return TimeSpan.FromTicks((long)elapsedTicks);
        }
    }
}
