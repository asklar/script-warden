using System.Text;

namespace ScriptWarden.Core;

/// <summary>
/// Tokenizes Windows command lines using the same rules as <c>CommandLineToArgvW</c> / the MSVCRT
/// runtime (backslash/quote handling), and supports stripping leading tokens while preserving the
/// exact original text of the remainder (used to forward the interpreter command line verbatim).
/// </summary>
public static class CommandLineParser
{
    /// <summary>A token and the [Start, End) span it occupied in the source command line.</summary>
    public readonly record struct TokenSpan(int Start, int End, string Value);

    /// <summary>Splits a command line into argument strings (quotes/backslashes resolved).</summary>
    public static string[] Tokenize(string commandLine)
    {
        var spans = Scan(commandLine);
        var result = new string[spans.Count];
        for (int i = 0; i < spans.Count; i++)
        {
            result[i] = spans[i].Value;
        }
        return result;
    }

    /// <summary>
    /// Returns the substring of <paramref name="commandLine"/> starting at the token with index
    /// <paramref name="count"/> (0-based), i.e. everything after the first <paramref name="count"/>
    /// tokens, with original quoting/spacing of the remainder preserved. Returns "" if there are
    /// not more than <paramref name="count"/> tokens.
    /// </summary>
    public static string StripLeadingTokens(string commandLine, int count)
    {
        var spans = Scan(commandLine);
        if (spans.Count <= count)
        {
            return "";
        }
        return commandLine[spans[count].Start..];
    }

    /// <summary>Scans a command line into token spans using CommandLineToArgvW-like rules.</summary>
    public static List<TokenSpan> Scan(string s)
    {
        var tokens = new List<TokenSpan>();
        int i = 0;
        int len = s.Length;
        var sb = new StringBuilder();

        while (true)
        {
            // Skip inter-token whitespace.
            while (i < len && (s[i] == ' ' || s[i] == '\t'))
            {
                i++;
            }
            if (i >= len)
            {
                break;
            }

            int start = i;
            sb.Clear();
            bool inQuotes = false;

            while (i < len)
            {
                char c = s[i];

                if ((c == ' ' || c == '\t') && !inQuotes)
                {
                    break;
                }

                if (c == '\\')
                {
                    int backslashes = 0;
                    while (i < len && s[i] == '\\')
                    {
                        backslashes++;
                        i++;
                    }

                    if (i < len && s[i] == '"')
                    {
                        // n backslashes before a quote: emit n/2 backslashes.
                        sb.Append('\\', backslashes / 2);
                        if ((backslashes & 1) == 1)
                        {
                            // Odd count -> the quote is escaped (literal).
                            sb.Append('"');
                            i++;
                        }
                        else if (inQuotes && i + 1 < len && s[i + 1] == '"')
                        {
                            // "" inside a quoted section -> literal quote, stay quoted.
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            inQuotes = !inQuotes;
                            i++;
                        }
                    }
                    else
                    {
                        sb.Append('\\', backslashes);
                    }
                    continue;
                }

                if (c == '"')
                {
                    if (inQuotes && i + 1 < len && s[i + 1] == '"')
                    {
                        sb.Append('"');
                        i += 2;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                        i++;
                    }
                    continue;
                }

                sb.Append(c);
                i++;
            }

            tokens.Add(new TokenSpan(start, i, sb.ToString()));
        }

        return tokens;
    }
}
