namespace Tms.Modules.TransportOps.Domain;

/// <summary>An amount in rupees and paise written in words the Indian way (thousand, lakh, crore), as printed on an invoice.</summary>
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    /// <summary>For example 186808.11 gives "Indian Rupees One Lakh Eighty Six Thousand Eight Hundred Eight and Eleven Paise Only".</summary>
    public static string Rupees(decimal amount)
    {
        var rounded = Math.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        var rupees = (long)Math.Floor(rounded);
        var paise = (int)Math.Round((rounded - rupees) * 100m, 0, MidpointRounding.AwayFromZero);

        var text = rupees == 0 && paise == 0 ? "Zero" : rupees == 0 ? string.Empty : Words(rupees);
        var result = $"Indian Rupees {text}".TrimEnd();
        if (paise > 0)
        {
            result += (rupees == 0 ? " " : " and ") + $"{Words(paise)} Paise";
        }

        return result + " Only";
    }

    private static string Words(long n)
    {
        var parts = new List<string>();
        void Take(long divisor, string name)
        {
            if (n >= divisor)
            {
                parts.Add($"{Below1000(n / divisor)} {name}");
                n %= divisor;
            }
        }

        Take(10_000_000, "Crore");
        Take(100_000, "Lakh");
        Take(1_000, "Thousand");
        if (n > 0)
        {
            parts.Add(Below1000(n));
        }

        return string.Join(' ', parts);
    }

    private static string Below1000(long n)
    {
        var words = new List<string>();
        if (n >= 100)
        {
            words.Add($"{Ones[n / 100]} Hundred");
            n %= 100;
        }

        if (n >= 20)
        {
            words.Add(Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : string.Empty));
        }
        else if (n > 0)
        {
            words.Add(Ones[n]);
        }

        return string.Join(' ', words);
    }
}
