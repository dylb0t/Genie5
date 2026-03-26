using System.Globalization;

namespace Genie4.Core.Utility;

public static class UtilityCore
{
    public static bool StringToBoolean(string? value)
    {
        return value switch
        {
            "True" or "true" or "1" => true,
            _ => false
        };
    }

    public static double StringToDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return -1;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : -1;
    }

    public static int StringToInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return -1;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : -1;
    }
}
