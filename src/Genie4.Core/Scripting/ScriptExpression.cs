using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Genie4.Core.Scripting;

/// <summary>
/// Recursive-descent expression evaluator for Genie script <c>if</c> conditions
/// and <c>eval</c>/<c>evalmath</c> assignments.
///
/// Supports:
///   - literals: numbers, "strings", true/false
///   - operators: || && ! == != = &lt; &gt; &lt;= &gt;= + - * / %
///   - functions: matchre(s,pat)*, contains, startswith, endswith,
///                tolower, toupper, len, count, abs, min, max
///   - bare identifiers: returned as the literal text (after %var/$var
///                       substitution by the engine)
///
/// (*) matchre populates $0..$9 capture-group vars on the instance.
/// </summary>
internal sealed class ScriptExpression
{
    private readonly string         _src;
    private readonly ScriptInstance _inst;
    private int _pos;

    private ScriptExpression(string src, ScriptInstance inst) { _src = src; _inst = inst; }

    public static object Eval(string src, ScriptInstance inst)
    {
        var p = new ScriptExpression(src, inst);
        var v = p.ParseOr();
        p.SkipWs();
        if (p._pos < p._src.Length)
            throw new Exception($"unexpected token at: {p._src[p._pos..]}");
        return v;
    }

    public static bool   EvalBool(string src, ScriptInstance inst) => ToBool(Eval(src, inst));
    public static string EvalString(string src, ScriptInstance inst) => ToStr(Eval(src, inst));

    // ── Coercion ────────────────────────────────────────────────────────────

    public static bool ToBool(object? v) => v switch
    {
        bool b   => b,
        double d => d != 0,
        string s => !string.IsNullOrEmpty(s)
                    && !s.Equals("false", StringComparison.OrdinalIgnoreCase)
                    && s != "0",
        null     => false,
        _        => false,
    };

    public static double ToNum(object? v) => v switch
    {
        double d => d,
        bool b   => b ? 1 : 0,
        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0,
        _        => 0,
    };

    public static string ToStr(object? v) => v switch
    {
        string s => s,
        bool b   => b ? "true" : "false",
        double d => d == Math.Floor(d) && !double.IsInfinity(d)
                        ? ((long)d).ToString(CultureInfo.InvariantCulture)
                        : d.ToString("0.################", CultureInfo.InvariantCulture),
        null     => "",
        _        => v.ToString() ?? "",
    };

    private static bool TryNum(object? v, out double n)
    {
        switch (v)
        {
            case double d: n = d; return true;
            case bool   b: n = b ? 1 : 0; return true;
            case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n);
        }
        n = 0; return false;
    }

    // ── Lexer helpers ───────────────────────────────────────────────────────

    private void SkipWs()
    {
        while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos])) _pos++;
    }

    private bool Match(string op)
    {
        SkipWs();
        if (_pos + op.Length > _src.Length) return false;
        if (_src.Substring(_pos, op.Length) != op) return false;
        _pos += op.Length;
        return true;
    }

    // ── Grammar ─────────────────────────────────────────────────────────────

    private object ParseOr()
    {
        var l = ParseAnd();
        while (Match("||"))
        {
            var r = ParseAnd();
            l = ToBool(l) || ToBool(r);
        }
        return l;
    }

    private object ParseAnd()
    {
        var l = ParseNot();
        while (Match("&&"))
        {
            var r = ParseNot();
            l = ToBool(l) && ToBool(r);
        }
        return l;
    }

    private object ParseNot()
    {
        SkipWs();
        if (_pos < _src.Length && _src[_pos] == '!'
            && (_pos + 1 >= _src.Length || _src[_pos + 1] != '='))
        {
            _pos++;
            return !ToBool(ParseNot());
        }
        return ParseCmp();
    }

    private object ParseCmp()
    {
        var l = ParseAdd();
        // Order matters: 2-char ops before 1-char.
        foreach (var op in new[] { "==", "!=", "<=", ">=", "=", "<", ">" })
        {
            int save = _pos;
            SkipWs();
            if (Match(op))
            {
                var r = ParseAdd();
                return Compare(l, r, op);
            }
            _pos = save;
        }
        return l;
    }

    private static object Compare(object l, object r, string op)
    {
        if (TryNum(l, out var a) && TryNum(r, out var b))
        {
            return op switch
            {
                "==" or "=" => a == b,
                "!="        => a != b,
                "<"         => a <  b,
                ">"         => a >  b,
                "<="        => a <= b,
                ">="        => a >= b,
                _           => false,
            };
        }
        var c = string.Compare(ToStr(l), ToStr(r), StringComparison.Ordinal);
        return op switch
        {
            "==" or "=" => c == 0,
            "!="        => c != 0,
            "<"         => c <  0,
            ">"         => c >  0,
            "<="        => c <= 0,
            ">="        => c >= 0,
            _           => false,
        };
    }

    private object ParseAdd()
    {
        var l = ParseMul();
        while (true)
        {
            int save = _pos;
            SkipWs();
            if (Match("+"))
            {
                var r = ParseMul();
                l = TryNum(l, out var a) && TryNum(r, out var b)
                        ? (object)(a + b)
                        : ToStr(l) + ToStr(r);
            }
            else if (Match("-"))
            {
                var r = ParseMul();
                l = ToNum(l) - ToNum(r);
            }
            else { _pos = save; break; }
        }
        return l;
    }

    private object ParseMul()
    {
        var l = ParseUnary();
        while (true)
        {
            int save = _pos;
            SkipWs();
            if (Match("*")) { var r = ParseUnary(); l = ToNum(l) * ToNum(r); }
            else if (Match("/"))
            {
                var r = ParseUnary();
                var d = ToNum(r);
                l = d == 0 ? 0 : ToNum(l) / d;
            }
            else if (Match("%"))
            {
                var r = ParseUnary();
                var d = ToNum(r);
                l = d == 0 ? 0 : ToNum(l) % d;
            }
            else { _pos = save; break; }
        }
        return l;
    }

    private object ParseUnary()
    {
        SkipWs();
        if (_pos < _src.Length && _src[_pos] == '-')
        {
            _pos++;
            return -ToNum(ParseUnary());
        }
        return ParseAtom();
    }

    private object ParseAtom()
    {
        SkipWs();
        if (_pos >= _src.Length) throw new Exception("expression: unexpected end");
        char c = _src[_pos];

        if (c == '(')
        {
            _pos++;
            var v = ParseOr();
            SkipWs();
            if (_pos >= _src.Length || _src[_pos] != ')')
                throw new Exception("expression: missing ')'");
            _pos++;
            return v;
        }
        if (c == '"')                              return ParseString();
        if (char.IsDigit(c) || c == '.')           return ParseNumber();
        if (char.IsLetter(c) || c == '_')          return ParseIdentOrCall();
        throw new Exception($"expression: unexpected '{c}'");
    }

    private string ParseString()
    {
        _pos++; // opening "
        var sb = new StringBuilder();
        while (_pos < _src.Length && _src[_pos] != '"')
        {
            if (_src[_pos] == '\\' && _pos + 1 < _src.Length)
            {
                _pos++;
                char esc = _src[_pos];
                sb.Append(esc switch
                {
                    'n'  => '\n',
                    't'  => '\t',
                    'r'  => '\r',
                    '\\' => '\\',
                    '"'  => '"',
                    _    => esc,
                });
                _pos++;
            }
            else
            {
                sb.Append(_src[_pos++]);
            }
        }
        if (_pos < _src.Length) _pos++; // closing "
        return sb.ToString();
    }

    private double ParseNumber()
    {
        int s = _pos;
        while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || _src[_pos] == '.')) _pos++;
        return double.Parse(_src[s.._pos], CultureInfo.InvariantCulture);
    }

    private object ParseIdentOrCall()
    {
        int s = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
            _pos++;
        var name = _src[s.._pos];

        SkipWs();
        if (_pos < _src.Length && _src[_pos] == '(')
        {
            _pos++;
            var args = new List<object>();
            SkipWs();
            if (_pos < _src.Length && _src[_pos] != ')')
            {
                args.Add(ParseOr());
                while (true)
                {
                    SkipWs();
                    if (_pos < _src.Length && _src[_pos] == ',') { _pos++; args.Add(ParseOr()); }
                    else break;
                }
            }
            SkipWs();
            if (_pos >= _src.Length || _src[_pos] != ')')
                throw new Exception($"expression: missing ')' after {name}(");
            _pos++;
            return CallFunc(name, args);
        }

        if (name.Equals("true",  StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return name; // bare identifier → literal text
    }

    private object CallFunc(string name, List<object> args)
    {
        string A(int i) => i < args.Count ? ToStr(args[i]) : "";
        double N(int i) => i < args.Count ? ToNum(args[i]) : 0;

        switch (name.ToLowerInvariant())
        {
            case "matchre":
            {
                var s   = A(0);
                var pat = A(1);
                Match m;
                try { m = Regex.Match(s, pat); }
                catch (Exception ex) { throw new Exception($"matchre: bad regex: {ex.Message}"); }
                if (!m.Success) return false;
                _inst.Vars["0"] = m.Value;
                for (int i = 1; i < m.Groups.Count && i <= 9; i++)
                    _inst.Vars[i.ToString()] = m.Groups[i].Value;
                return true;
            }
            case "contains":   return A(0).IndexOf(A(1), StringComparison.OrdinalIgnoreCase) >= 0;
            case "startswith": return A(0).StartsWith(A(1), StringComparison.OrdinalIgnoreCase);
            case "endswith":   return A(0).EndsWith(A(1),   StringComparison.OrdinalIgnoreCase);
            case "tolower":    return A(0).ToLowerInvariant();
            case "toupper":    return A(0).ToUpperInvariant();
            case "len":
            case "length":     return (double)A(0).Length;
            case "count":
            {
                var s = A(0); var sep = A(1);
                if (sep.Length == 0) return 0.0;
                int n = 0, idx = 0;
                while ((idx = s.IndexOf(sep, idx, StringComparison.Ordinal)) >= 0)
                { n++; idx += sep.Length; }
                return (double)n;
            }
            case "abs": return Math.Abs(N(0));
            case "min": return Math.Min(N(0), N(1));
            case "max": return Math.Max(N(0), N(1));
        }
        throw new Exception($"unknown function: {name}");
    }
}
