using System.Globalization;

namespace EgRoomTracker.Application.Generic;

/// <summary>
/// Tri naturel d'un numéro de local : les suites de chiffres se comparent comme des nombres (<c>2</c> avant <c>10</c>), le reste sans tenir
/// compte de la casse. Un numéro vide passe en dernier.
/// </summary>
public sealed class NaturalOrder : IComparer<string?>
{
    public static readonly NaturalOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        var emptyX = string.IsNullOrWhiteSpace(x);
        var emptyY = string.IsNullOrWhiteSpace(y);
        if (emptyX || emptyY)
        {
            return emptyX == emptyY ? 0 : emptyX ? 1 : -1;
        }

        var a = x!.Trim();
        var b = y!.Trim();
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var startA = i;
                var startB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                var digitsA = a[startA..i].TrimStart('0');
                var digitsB = b[startB..j].TrimStart('0');
                var byLength = digitsA.Length.CompareTo(digitsB.Length);
                if (byLength != 0)
                {
                    return byLength;
                }

                var byValue = string.CompareOrdinal(digitsA, digitsB);
                if (byValue != 0)
                {
                    return byValue;
                }
            }
            else
            {
                var byChar = char.ToUpper(a[i], CultureInfo.InvariantCulture).CompareTo(char.ToUpper(b[j], CultureInfo.InvariantCulture));
                if (byChar != 0)
                {
                    return byChar;
                }

                i++;
                j++;
            }
        }

        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>Départage stable par ElementId (numérique si possible, sinon texte).</summary>
    public static int CompareElementIds(string x, string y) =>
        long.TryParse(x, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var a) && long.TryParse(y, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var b)
            ? a.CompareTo(b)
            : string.CompareOrdinal(x, y);
}
