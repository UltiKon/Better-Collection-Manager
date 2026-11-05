using System;

namespace BetterCollectionManager
{
    /// <summary>Immutable card sets: each machine word combines 64 native results.</summary>
    internal sealed class FilterBitSet
    {
        private readonly ulong[] m_words;
        public int Count { get; }
        private FilterBitSet(int count, ulong[] words) { Count = count; m_words = words; }
        public static FilterBitSet Empty(int count) => new FilterBitSet(count, new ulong[(count + 63) / 64]);
        public static FilterBitSet Full(int count)
        {
            var words = new ulong[(count + 63) / 64];
            for (int i = 0; i < words.Length; i++) words[i] = ulong.MaxValue;
            if (words.Length > 0 && (count & 63) != 0) words[words.Length - 1] = (1UL << (count & 63)) - 1UL;
            return new FilterBitSet(count, words);
        }
        public static FilterBitSet Build(int count, Func<int, bool> matches)
        {
            var words = new ulong[(count + 63) / 64];
            for (int i = 0; i < count; i++) if (matches(i)) words[i >> 6] |= 1UL << (i & 63);
            return new FilterBitSet(count, words);
        }
        public bool Contains(int index) => index >= 0 && index < Count && (m_words[index >> 6] & (1UL << (index & 63))) != 0;
        public FilterBitSet And(FilterBitSet other) => Combine(other, true);
        public FilterBitSet Or(FilterBitSet other) => Combine(other, false);
        public FilterBitSet Without(int index)
        {
            if (index < 0 || index >= Count) return this;
            var words = (ulong[])m_words.Clone();
            words[index >> 6] &= ~(1UL << (index & 63));
            return new FilterBitSet(Count, words);
        }
        public FilterBitSet Not()
        {
            var words = new ulong[m_words.Length];
            for (int i = 0; i < words.Length; i++) words[i] = ~m_words[i];
            if (words.Length > 0 && (Count & 63) != 0) words[words.Length - 1] &= (1UL << (Count & 63)) - 1UL;
            return new FilterBitSet(Count, words);
        }
        private FilterBitSet Combine(FilterBitSet other, bool and)
        {
            if (Count != other.Count) throw new ArgumentException("Card sets have different universes");
            var words = new ulong[m_words.Length];
            for (int i = 0; i < words.Length; i++) words[i] = and ? m_words[i] & other.m_words[i] : m_words[i] | other.m_words[i];
            return new FilterBitSet(Count, words);
        }
        public int IntersectionCount(FilterBitSet other)
        {
            if (Count != other.Count) throw new ArgumentException("Card sets have different universes");
            int total = 0;
            for (int i = 0; i < m_words.Length; i++) total += PopulationCount(m_words[i] & other.m_words[i]);
            return total;
        }
        private static int PopulationCount(ulong word)
        {
            // Portable to the game's .NET Framework runtime (no BitOperations dependency).
            word -= (word >> 1) & 0x5555555555555555UL;
            word = (word & 0x3333333333333333UL) + ((word >> 2) & 0x3333333333333333UL);
            word = (word + (word >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)(unchecked(word * 0x0101010101010101UL) >> 56);
        }
        public bool Intersects(FilterBitSet other) => Intersects(other, false);
        public bool Intersects(FilterBitSet other, bool negateThis)
        {
            if (Count != other.Count) throw new ArgumentException("Card sets have different universes");
            for (int i = 0; i < m_words.Length; i++) if (((negateThis ? ~m_words[i] : m_words[i]) & other.m_words[i]) != 0) return true;
            return false;
        }
    }
}
