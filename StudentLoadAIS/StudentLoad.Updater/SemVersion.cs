using System.Text.RegularExpressions;

namespace StudentLoad.Updater;

internal sealed class SemVersion : IComparable<SemVersion>
{
    private static readonly Regex Pattern = new(
        @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly string[] numbers;
    private readonly string[] prerelease;

    private SemVersion(string value, Match match)
    {
        Value = value;
        numbers = [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value];
        prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
    }

    public string Value { get; }

    public static SemVersion Parse(string value)
    {
        var match = Pattern.Match(value);
        if (!match.Success)
            throw new FormatException($"Некорректная версия SemVer: {value}.");

        var version = new SemVersion(value, match);
        if (version.prerelease.Any(id => IsNumeric(id) && id.Length > 1 && id[0] == '0'))
            throw new FormatException($"Числовой идентификатор SemVer начинается с нуля: {value}.");
        return version;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
            return 1;

        for (var i = 0; i < numbers.Length; i++)
        {
            var comparison = CompareNumeric(numbers[i], other.numbers[i]);
            if (comparison != 0)
                return comparison;
        }

        if (prerelease.Length == 0 || other.prerelease.Length == 0)
            return (prerelease.Length == 0).CompareTo(other.prerelease.Length == 0);

        for (var i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
        {
            var left = prerelease[i];
            var right = other.prerelease[i];
            var leftNumeric = IsNumeric(left);
            var rightNumeric = IsNumeric(right);
            var comparison = (leftNumeric, rightNumeric) switch
            {
                (true, true) => CompareNumeric(left, right),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left, right)
            };
            if (comparison != 0)
                return comparison;
        }
        return prerelease.Length.CompareTo(other.prerelease.Length);
    }

    private static bool IsNumeric(string value) => value.All(char.IsAsciiDigit);

    private static int CompareNumeric(string left, string right)
    {
        var lengthComparison = left.Length.CompareTo(right.Length);
        return lengthComparison == 0 ? string.CompareOrdinal(left, right) : lengthComparison;
    }
}

