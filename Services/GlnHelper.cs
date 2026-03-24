namespace EdifactValidator.Services;

/// <summary>Utility methods for GLN / EAN-13 validation.</summary>
public static class GlnHelper
{
    /// <summary>
    /// Returns true if <paramref name="gln"/> is a syntactically valid EAN-13 / GLN:
    /// exactly 13 digits and the check digit (position 13) is correct.
    /// </summary>
    public static bool IsValid(string? gln)
    {
        if (string.IsNullOrWhiteSpace(gln) || gln.Length != 13) return false;
        foreach (var c in gln) if (c < '0' || c > '9') return false;

        // EAN-13 check digit: alternating weights 1 and 3 over first 12 digits
        int sum = 0;
        for (int i = 0; i < 12; i++)
            sum += (gln[i] - '0') * (i % 2 == 0 ? 1 : 3);

        int expected = (10 - (sum % 10)) % 10;
        return (gln[12] - '0') == expected;
    }
}
