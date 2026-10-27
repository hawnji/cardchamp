using System;
using System.Collections.Generic;

namespace CardVault.Core.Common
{
    /// <summary>
    /// SplitMix64 generator whose entire state is a single persisted value. Saving and
    /// reloading the state reproduces the exact same sequence, which keeps pack odds,
    /// NPC behaviour and grading deterministic across restarts and in tests.
    /// </summary>
    public sealed class DeterministicRng
    {
        private readonly RngState _state;

        public DeterministicRng(RngState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public ulong NextULong()
        {
            unchecked
            {
                _state.Value += 0x9E3779B97F4A7C15UL;
                ulong z = _state.Value;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }

        public double Range(double minInclusive, double maxExclusive)
            => minInclusive + NextDouble() * (maxExclusive - minInclusive);

        public bool Chance(double probability) => NextDouble() < probability;

        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0) throw new InvalidOperationException("Cannot pick from an empty list.");
            return items[Range(0, items.Count)];
        }

        /// <summary>Weighted pick. Returns -1 when every weight is zero.</summary>
        public int PickWeightedIndex(IReadOnlyList<double> weights)
        {
            double total = 0;
            for (int i = 0; i < weights.Count; i++) total += Math.Max(0, weights[i]);
            if (total <= 0) return -1;
            double roll = NextDouble() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                double w = Math.Max(0, weights[i]);
                if (roll < w) return i;
                roll -= w;
            }
            return weights.Count - 1;
        }

        /// <summary>Stateless hash used to derive per-entity seeds without consuming the main stream.</summary>
        public static ulong Hash(string text)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                foreach (char c in text)
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }
                return h;
            }
        }
    }

    public sealed class RngState
    {
        public ulong Value;
    }
}
