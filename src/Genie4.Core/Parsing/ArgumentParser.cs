namespace Genie4.Core.Parsing;

public static class ArgumentParser
{
    public static IReadOnlyList<string> ParseArgs(string text)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        var current = "";
        var inQuotes = false;

        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    results.Add(current);
                    current = "";
                }
            }
            else
            {
                current += ch;
            }
        }

        if (current.Length > 0)
        {
            results.Add(current);
        }

        return results;
    }
}
