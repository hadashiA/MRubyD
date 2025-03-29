using System.Text;

namespace MRubyCS.SourceGenerator;

public class NamingRule
{
    // キャメルケースからスネークケースに変換
    public static string CamelCaseToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLower(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    // スネークケースからキャメルケースに変換 (lowerCamelCase)
    public static string SnakeCaleToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder();
        var parts = input.Split('_');
        foreach (var part in parts)
        {
            if (parts.Length > 0)
            {
                sb.Append(char.ToUpper(part[0]) + part.Substring(1).ToLower());
            }
        }
        return sb.ToString();
    }
}