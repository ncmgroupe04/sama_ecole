using System.Globalization;

namespace SamaEcole.Application.Common.Extensions;

public static class StringExtensions
{
    public static string ToTitleCase(this string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var textInfo = new CultureInfo("fr-FR").TextInfo;
        var lower = text.Trim().ToLowerInvariant();
        var titleCased = textInfo.ToTitleCase(lower);
        
        var parts = titleCased.Split('-');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }
        }
        
        return string.Join("-", parts);
    }
}
