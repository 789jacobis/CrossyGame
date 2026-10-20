using System;
using System.Globalization;
using System.Text;

namespace CrossyRoadServer;

public static class PlayerNamePolicy
{
    public static bool TryNormalize(
        string? requestedName,
        out string displayName,
        out string nameKey)
    {
        try
        {
            displayName = (requestedName ?? string.Empty).Trim()
                .Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            displayName = string.Empty;
            nameKey = string.Empty;
            return false;
        }
        nameKey = string.Empty;

        int length = new StringInfo(displayName).LengthInTextElements;
        if (length < 1 || length > 12)
        {
            return false;
        }

        foreach (char character in displayName)
        {
            if (!char.IsLetterOrDigit(character) &&
                character != '_' && character != '-')
            {
                return false;
            }
        }

        nameKey = displayName.ToUpperInvariant();
        return true;
    }
}
