using System.Text;

namespace AMSpeedUp.Core.Inventory;

public static class StoreAppNames
{
    /// <summary>
    /// Makes a readable name from a Store package name, for example
    /// "king.com.CandyCrushSodaSaga" becomes "Candy Crush Soda Saga". Used when a preset entry
    /// covers several apps, so each one still gets its own name in the list.
    /// </summary>
    public static string Friendly(string packageName)
    {
        var lastDot = packageName.LastIndexOf('.');
        var last = lastDot >= 0 && lastDot < packageName.Length - 1 ? packageName[(lastDot + 1)..] : packageName;

        var builder = new StringBuilder(last.Length + 8);
        for (var i = 0; i < last.Length; i++)
        {
            var c = last[i];
            var startsWord = i > 0 && char.IsUpper(c) && (char.IsLower(last[i - 1]) || (i + 1 < last.Length && char.IsLower(last[i + 1]) && char.IsUpper(last[i - 1])));
            var digitBoundary = i > 0 && (char.IsDigit(c) != char.IsDigit(last[i - 1])) && char.IsLetterOrDigit(last[i - 1]) && (char.IsDigit(c) || char.IsUpper(c));
            if (startsWord || digitBoundary)
            {
                builder.Append(' ');
            }

            builder.Append(c);
        }

        var result = builder.ToString().Replace('-', ' ').Trim();
        return result.Length == 0 ? packageName : result;
    }
}
