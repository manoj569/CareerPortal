namespace JobPortal.Application.Common.Text;

using System.Globalization;
using System.Text;

public static class PersonalName
{
    public static bool TrySplit(
        string? value,
        out string firstName,
        out string lastName)
    {
        firstName = string.Empty;
        lastName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (normalized.Length > 201 ||
            normalized.Contains("  ", StringComparison.Ordinal) ||
            normalized.EnumerateRunes().Any(rune => rune.Value != ' ' &&
                !IsUnicodeNameCharacter(rune)))
            return false;

        var words = normalized.Split(' ', StringSplitOptions.None);
        firstName = words[0];
        lastName = string.Join(' ', words.Skip(1));
        return firstName.Length is > 0 and <= 100 && lastName.Length <= 100;
    }

    private static bool IsUnicodeNameCharacter(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark;
}
