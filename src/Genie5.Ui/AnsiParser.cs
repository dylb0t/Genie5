using System.Text;

namespace Genie5.Ui;

public static class AnsiParser
{
    /// <summary>Parse a string that may contain ANSI escape codes into a RenderLine.</summary>
    public static RenderLine Parse(string input,
        bool gslBold = false, string gslColor = "")
    {
        var line = new RenderLine();
        var sb   = new StringBuilder();
        var fg   = string.IsNullOrEmpty(gslColor) ? "Default" : gslColor;
        var bold = gslBold;

        void Flush()
        {
            if (sb.Length == 0) return;
            line.Spans.Add(new AnsiSpan
            {
                Text       = sb.ToString(),
                Foreground = fg,
                Bold       = bold
            });
            sb.Clear();
        }

        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '\x1b' && i + 1 < input.Length && input[i + 1] == '[')
            {
                Flush();

                int m = input.IndexOf('m', i);
                if (m == -1) break;

                var codes = input.Substring(i + 2, m - i - 2).Split(';');

                foreach (var code in codes)
                {
                    if (!int.TryParse(code, out var n)) continue;
                    switch (n)
                    {
                        case 0:
                            fg   = string.IsNullOrEmpty(gslColor) ? "Default" : gslColor;
                            bold = gslBold;
                            break;
                        case 1:  bold = true;      break;
                        case 30: fg = "Black";     break;
                        case 31: fg = "Red";       break;
                        case 32: fg = "Green";     break;
                        case 33: fg = "Yellow";    break;
                        case 34: fg = "Blue";      break;
                        case 35: fg = "Magenta";   break;
                        case 36: fg = "Cyan";      break;
                        case 37: fg = "White";     break;
                    }
                }

                i = m;
            }
            else
            {
                sb.Append(input[i]);
            }
        }

        Flush();
        return line;
    }
}
