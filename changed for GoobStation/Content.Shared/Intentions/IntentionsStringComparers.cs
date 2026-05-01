using System;
using System.Collections.Generic;

namespace Content.Shared.Intentions;

/// <summary>
/// Sandbox-safe ordinal comparers used by Intentions collections and ordering.
/// </summary>
public static class IntentionsStringComparers
{
    public static IEqualityComparer<string> Equality { get; } = new OrdinalEqualityComparer();

    public static IComparer<string> Ordering { get; } = new OrdinalOrderingComparer();

    private sealed class OrdinalEqualityComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
        {
            return string.Equals(x, y, StringComparison.Ordinal);
        }

        public int GetHashCode(string obj)
        {
            return obj.GetHashCode();
        }
    }

    private sealed class OrdinalOrderingComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x is null)
                return -1;

            if (y is null)
                return 1;

            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}
