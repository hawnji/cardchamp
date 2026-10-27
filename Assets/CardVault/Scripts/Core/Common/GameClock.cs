using System;

namespace CardVault.Core.Common
{
    /// <summary>
    /// Persisted state of the internal game calendar.
    /// </summary>
    public sealed class ClockState
    {
        /// <summary>Seconds elapsed on the game calendar since the content epoch.</summary>
        public long GameSeconds;

        /// <summary>Highest device UTC time ever observed (ticks). Never moves backwards.</summary>
        public long HighestObservedUtcTicks;

        /// <summary>Game seconds per real second. 1 in normal play; larger values accelerate the calendar for testing.</summary>
        public double TimeScale = 1.0;

        /// <summary>Maximum game time credited for a single gap between syncs (offline time, clock jumps).</summary>
        public long MaxAdvancePerSyncSeconds = GameClock.DefaultMaxAdvancePerSyncSeconds;

        public int RollbacksDetected;
        public int ForwardJumpsClamped;
        public long DeveloperAdvancedSeconds;
    }

    /// <summary>
    /// Offline-first, tamper-resistant game clock.
    ///
    /// Rules:
    /// 1. Game time only moves forward.
    /// 2. Real time is measured against the highest device time ever seen, so winding the
    ///    device clock back and then forward again does not replay the same interval.
    /// 3. A single gap is capped (default 72h), so a large forward jump only advances the
    ///    calendar by a bounded amount.
    /// 4. Rewards and stock are keyed by IDs and recorded once, so even legitimate time
    ///    advancement can never re-grant a reward or recreate stock.
    /// </summary>
    public sealed class GameClock
    {
        public const long DefaultMaxAdvancePerSyncSeconds = 72 * 3600;
        public const long SecondsPerDay = 86400;

        private readonly ClockState _state;
        private readonly DateTime _epochUtc;

        public GameClock(ClockState state, DateTime epochUtc)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _epochUtc = DateTime.SpecifyKind(epochUtc, DateTimeKind.Utc);
        }

        public long Now => _state.GameSeconds;
        public ClockState State => _state;
        public DateTime EpochUtc => _epochUtc;

        public long Day => Now / SecondsPerDay;

        public DateTime ToCalendarDate(long gameSeconds) => _epochUtc.AddSeconds(gameSeconds);
        public DateTime CalendarNow => ToCalendarDate(Now);

        public long FromCalendarDate(DateTime utc) => (long)(DateTime.SpecifyKind(utc, DateTimeKind.Utc) - _epochUtc).TotalSeconds;

        /// <summary>
        /// Advances game time using the device clock. Returns the number of game seconds added.
        /// </summary>
        public long SyncWithDevice(DateTime deviceUtcNow)
        {
            long nowTicks = deviceUtcNow.ToUniversalTime().Ticks;
            if (_state.HighestObservedUtcTicks == 0)
            {
                _state.HighestObservedUtcTicks = nowTicks;
                return 0;
            }

            long deltaTicks = nowTicks - _state.HighestObservedUtcTicks;
            if (deltaTicks <= 0)
            {
                if (deltaTicks < -TimeSpan.TicksPerMinute) _state.RollbacksDetected++;
                return 0;
            }

            _state.HighestObservedUtcTicks = nowTicks;
            double realSeconds = deltaTicks / (double)TimeSpan.TicksPerSecond;
            long gameDelta = (long)Math.Floor(realSeconds * Math.Max(0, _state.TimeScale));
            if (gameDelta > _state.MaxAdvancePerSyncSeconds)
            {
                gameDelta = _state.MaxAdvancePerSyncSeconds;
                _state.ForwardJumpsClamped++;
            }

            _state.GameSeconds += gameDelta;
            return gameDelta;
        }

        /// <summary>
        /// Developer/test hook for the accelerated calendar. Moves game time forward only.
        /// </summary>
        public void AdvanceBy(long gameSeconds)
        {
            if (gameSeconds <= 0) return;
            _state.GameSeconds += gameSeconds;
            _state.DeveloperAdvancedSeconds += gameSeconds;
        }

        public void SetTimeScale(double scale) => _state.TimeScale = Math.Max(0, scale);
    }
}
