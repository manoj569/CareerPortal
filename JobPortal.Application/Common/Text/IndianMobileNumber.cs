namespace JobPortal.Application.Common.Text;

public static class IndianMobileNumber
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            return false;

        var input = value.Trim();
        if (input.Any(character =>
                !char.IsDigit(character) &&
                character is not '+' and not ' ' and not '-' and not '(' and not ')'))
            return false;
        if (input.Count(character => character == '+') > 1 ||
            (input.Contains('+') && input[0] != '+'))
            return false;
        if (input.Contains("--", StringComparison.Ordinal) ||
            input.Count(character => character == '(') > 1 ||
            input.Count(character => character == ')') > 1 ||
            input.Count(character => character == '(') !=
            input.Count(character => character == ')') ||
            (input.Contains('(') && input.IndexOf('(') > input.IndexOf(')')))
            return false;

        var digits = new string(input.Where(char.IsDigit).ToArray());
        var nationalNumber = digits switch
        {
            { Length: 10 } => digits,
            { Length: 11 } when digits[0] == '0' => digits[1..],
            { Length: 12 } when digits.StartsWith("91", StringComparison.Ordinal) => digits[2..],
            _ => string.Empty
        };

        if (nationalNumber.Length != 10 ||
            nationalNumber[0] is < '6' or > '9' ||
            nationalNumber.All(character => character == nationalNumber[0]) ||
            nationalNumber[..5] == nationalNumber[5..])
            return false;

        normalized = $"+91{nationalNumber}";
        return true;
    }
}
