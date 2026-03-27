using System.Text;

namespace Genie5.Ui;

public static class AnsiParser
{
    public static RenderLine Parse(string input)
    {
        var line = new RenderLine();

        var sb = new StringBuilder();
        var fg = "Default";
        var bold = false;

        void Flush()
        {
            if (sb.Length == 0) return;

            line.Spans.Add(new AnsiSpan
            {
                Text = sb.ToString(),
                Foreground = fg,
                Bold = bold
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

                foreach (var c in codes)
                {
                    if (!int.TryParse(c, out var n)) continue;

                    switch (n)
                    {
                        case 0: fg = "Default"; bold = false; break;
                        case 1: bold = true; break;
                        case 31: fg = "Red"; break;
                        case 32: fg = "Green"; break;
                        case 33: fg = "Yellow"; break;
                        case 34: fg = "Blue"; break;
                        case 36: fg = "Cyan"; break;
                        case 37: fg = "White"; break;
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