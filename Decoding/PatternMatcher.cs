namespace Gocgun.Diagnostics.Shared.Decoding;

/// <summary>Matches a key glob that contains only <c>*</c>.</summary>
internal static class PatternMatcher
{
    internal static bool IsMatch(string pattern, string value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);
        var patternIndex = 0;
        var valueIndex = 0;
        var star = -1;
        var match = 0;
        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == value[valueIndex])
            {
                patternIndex++;
                valueIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                star = patternIndex;
                patternIndex++;
                match = valueIndex;
                continue;
            }

            if (star >= 0)
            {
                patternIndex = star + 1;
                match++;
                valueIndex = match;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    internal static string? FindContract(IReadOnlyList<PatternMap> maps, string key)
    {
        return maps.FirstOrDefault(map => IsMatch(map.Pattern, key))?.ContractType;
    }
}
