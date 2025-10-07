using System.Collections.Generic;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Tokenizer;
using System.Text;

namespace Bonsai.Scripting.Expressions
{
    internal static class CompatibilityAnalyzer
    {
        internal static readonly Dictionary<string, string> LegacyKeywords = new()
        {
            { "boolean", "bool" },
            { "datetime", "DateTime" },
            { "datetimeoffset", "DateTimeOffset" },
            { "guid", "Guid" },
            { "int16", "short" },
            { "int32", "int" },
            { "int64", "long" },
            { "single", "float" },
            { "timespan", "TimeSpan" },
            { "uint32", "uint" },
            { "uint64", "ulong" },
            { "uint16", "ushort" },
            { "math", "Math" },
            { "convert", "Convert" }
        };

        public static bool ReplaceLegacyKeywords(ParsingConfig? parsingConfig, string text, out string result)
        {
            result = text;
            if (string.IsNullOrEmpty(text))
                return false;


            List<(Token, string)> replacements = null;
            var previousTokenId = TokenId.Unknown;
            var textParser = new TextParser(parsingConfig, text);
            while (textParser.CurrentToken.Id != TokenId.End)
            {
                if (textParser.CurrentToken.Id == TokenId.Identifier &&
                    previousTokenId != TokenId.Dot &&
                    LegacyKeywords.TryGetValue(textParser.CurrentToken.Text, out var keyword))
                {
                    replacements ??= new();
                    replacements.Add((textParser.CurrentToken, keyword));
                }

                previousTokenId = textParser.CurrentToken.Id;
                textParser.NextToken();
            }

            if (replacements?.Count > 0)
            {
                var sb = new StringBuilder(text);
                for (int i = replacements.Count - 1; i >= 0; i--)
                {
                    var (token, keyword) = replacements[i];
                    sb.Remove(token.Pos, token.Text.Length);
                    sb.Insert(token.Pos, keyword);
                }

                result = sb.ToString();
                return true;
            }

            return false;
        }
    }
}
