using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Config;
using System.Linq.Dynamic.Core.Exceptions;
using System.Linq.Dynamic.Core.Tokenizer;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace Bonsai.Scripting.Expressions.Design
{
    /// <summary>
    /// Analyzes the expression at selected caret position to determine the nearest declaring type.
    /// Some parts of the code is based on https://github.com/zzzprojects/System.Linq.Dynamic.Core
    /// and licensed under the Apache 2.0 license.
    /// </summary>
    internal class CaretExpressionAnalyzer
    {
        private static readonly string[] OutKeywords = ["out", "$out"];
        private const string DiscardVariable = "_";
        private const char DotCharacter = '.';
        internal static readonly Type[] DefaultTypes = new[]
        {
            typeof(Object),
            typeof(Boolean),
            typeof(Char),
            typeof(String),
            typeof(SByte),
            typeof(Byte),
            typeof(Int16),
            typeof(UInt16),
            typeof(Int32),
            typeof(UInt32),
            typeof(Int64),
            typeof(UInt64),
            typeof(Single),
            typeof(Double),
            typeof(Decimal),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(TimeSpan),
            typeof(Guid),
            typeof(Math),
            typeof(Convert)
        };

        readonly ParsingConfig _parsingConfig;
        readonly TextParser _textParser;
        readonly Stack<int> _primaryStack;
        readonly string _text;
        
        public CaretExpressionAnalyzer(ParsingConfig config, string text, int position)
        {
            if (!string.IsNullOrEmpty(text) && position > 0 && text[position - 1] == DotCharacter)
                position = position - 1;

            _parsingConfig = config;
            _text = position > 0 ? text.Substring(0, position) : string.Empty;
            _textParser = new TextParser(_parsingConfig, _text);
            _primaryStack = new Stack<int>();
        }

        public Type ParseExpressionType(Type itType, out bool isClassIdentifier)
        {
            _primaryStack.Clear();
            try { ParseConditionalOperator(); }
            catch (ParseException) { }

            isClassIdentifier = false;
            var primaryText = _text.Substring(_primaryStack.FirstOrDefault());
            try
            {
                return !string.IsNullOrEmpty(primaryText)
                    ? DynamicExpressionParser.ParseLambda(_parsingConfig, itType, null, primaryText).ReturnType
                    : null;
            }
            catch (ParseException pex)
            {
                isClassIdentifier = true;
                return DefaultTypes.Append(itType).FirstOrDefault(
                    type => primaryText.Equals(type.Name, StringComparison.OrdinalIgnoreCase))
                    ?? throw pex;
            }
        }

        // out keyword
        private void ParseOutKeyword()
        {
            if (_textParser.CurrentToken.Id == TokenId.Identifier && OutKeywords.Contains(_textParser.CurrentToken.Text))
            {
                // Go to next token (which should be a '_')
                _textParser.NextToken();

                var variableName = _textParser.CurrentToken.Text;
                if (variableName != DiscardVariable)
                {
                    throw ParseError("OutKeywordRequiresDiscard");
                }

                // Advance to next token
                _textParser.NextToken();
            }

            ParseConditionalOperator();
        }

        // ?: operator
        private void ParseConditionalOperator()
        {
            int errorPos = _textParser.CurrentToken.Pos;
            ParseNullCoalescingOperator();
            if (_textParser.CurrentToken.Id == TokenId.Question)
            {
                _textParser.NextToken();
                ParseConditionalOperator();
                _textParser.ValidateToken(TokenId.Colon, "ColonExpected");
                _textParser.NextToken();
                ParseConditionalOperator();
            }
        }

        // ?? (null-coalescing) operator
        private void ParseNullCoalescingOperator()
        {
            ParseLambdaOperator();
            if (_textParser.CurrentToken.Id == TokenId.NullCoalescing)
            {
                _textParser.NextToken();
                ParseConditionalOperator();
            }
        }

        // => operator - Added Support for projection operator
        private void ParseLambdaOperator()
        {
            ParseOrOperator();

            if (_textParser.CurrentToken.Id == TokenId.Lambda)
            {
                _textParser.NextToken();
                if (_textParser.CurrentToken.Id is TokenId.Identifier or TokenId.OpenParen)
                {
                    ParseConditionalOperator();
                }
                _textParser.ValidateToken(TokenId.OpenParen, "OpenParenExpected");
            }
        }

        // Or operator
        // - ||
        // - Or
        // - OrElse
        private void ParseOrOperator()
        {
            ParseAndOperator();
            while (_textParser.CurrentToken.Id == TokenId.DoubleBar)
            {
                Token op = _textParser.CurrentToken;
                _textParser.NextToken();
                ParseAndOperator();
            }
        }

        // And operator
        // - &&
        // - And
        // - AndAlso
        private void ParseAndOperator()
        {
            ParseIn();
            while (_textParser.CurrentToken.Id == TokenId.DoubleAmpersand)
            {
                Token op = _textParser.CurrentToken;
                _textParser.NextToken();
                ParseIn();
            }
        }

        // "in" / "not in" / "not_in" operator for literals - example: "x in (1,2,3,4)"
        // "in" / "not in" / "not_in" operator to mimic contains - example: "x in @0", compare to @0.Contains(x)
        private void ParseIn()
        {
            ParseLogicalAndOrOperator();
            while (_textParser.TryGetToken(["in", "not_in", "not"], [TokenId.Exclamation], out var token))
            {
                if (token.Text == "not" || token.Id == TokenId.Exclamation)
                {
                    _textParser.NextToken();

                    if (!TokenIsIdentifier("in"))
                    {
                        throw ParseError(token.Pos, "TokenExpected", "in");
                    }
                }

                _textParser.NextToken();

                if (_textParser.CurrentToken.Id == TokenId.OpenParen) // literals (or other inline list)
                {
                    while (_textParser.CurrentToken.Id != TokenId.CloseParen)
                    {
                        _textParser.NextToken();

                        // we need to parse unary expressions because otherwise 'in' clause will fail in use cases like 'in (-1, -1)' or 'in (!true)'
                        ParseUnary();

                        if (_textParser.CurrentToken.Id == TokenId.End)
                        {
                            throw ParseError(token.Pos, "CloseParenOrCommaExpected");
                        }
                    }

                    // Since this started with an open paren, make sure to move off the close
                    _textParser.NextToken();
                }
                else if (_textParser.CurrentToken.Id == TokenId.Identifier) // a single argument
                {
                    ParsePrimary();
                }
                else
                {
                    throw ParseError(token.Pos, "OpenParenOrIdentifierExpected");
                }
            }
        }

        // &, | bitwise operators
        private void ParseLogicalAndOrOperator()
        {
            ParseComparisonOperator();

            while (_textParser.CurrentToken.Id is TokenId.Ampersand or TokenId.Bar)
            {
                Token op = _textParser.CurrentToken;
                _textParser.NextToken();
                ParseComparisonOperator();
            }
        }

        // =, ==, !=, <>, >, >=, <, <= operators
        private void ParseComparisonOperator()
        {
            ParseShiftOperator();
            while (_textParser.CurrentToken.Id.IsComparisonOperator())
            {
                Token op = _textParser.CurrentToken;
                _textParser.NextToken();
                ParseShiftOperator();
            }
        }

        // <<, >> operators
        private void ParseShiftOperator()
        {
            ParseAdditive();
            while (_textParser.CurrentToken.Id == TokenId.DoubleLessThan || _textParser.CurrentToken.Id == TokenId.DoubleGreaterThan)
            {
                Token op = _textParser.CurrentToken;
                _textParser.NextToken();
                ParseAdditive();
            }
        }

        // +, - operators
        private void ParseAdditive()
        {
            ParseArithmetic();
            while (_textParser.CurrentToken.Id is TokenId.Plus or TokenId.Minus)
            {
                Token op = _textParser.CurrentToken;
                _textParser.NextToken();
                ParseArithmetic();
            }
        }

        // *, /, %, mod operators
        private void ParseArithmetic()
        {
            ParseUnary();
            while (_textParser.CurrentToken.Id is TokenId.Asterisk or TokenId.Slash or TokenId.Percent || TokenIsIdentifier("mod"))
            {
                Token op = _textParser.CurrentToken;
                _textParser.NextToken();
                ParseUnary();
            }
        }

        // -, !, not unary operators
        private void ParseUnary()
        {
            if (_textParser.CurrentToken.Id == TokenId.Minus || _textParser.CurrentToken.Id == TokenId.Exclamation || TokenIsIdentifier("not"))
            {
                Token op = _textParser.CurrentToken;
                _textParser.NextToken();
                if (op.Id == TokenId.Minus && _textParser.CurrentToken.Id is TokenId.IntegerLiteral or TokenId.RealLiteral)
                {
                    _textParser.CurrentToken.Text = "-" + _textParser.CurrentToken.Text;
                    _textParser.CurrentToken.Pos = op.Pos;
                    ParsePrimary();
                }

                ParseUnary();
            }

            ParsePrimary();
        }

        // primary elements
        private void ParsePrimary()
        {
            _primaryStack.Push(_textParser.CurrentToken.Pos);
            ParsePrimaryStart();

            while (true)
            {
                if (_textParser.CurrentToken.Id == TokenId.Dot)
                {
                    _textParser.NextToken();
                    ParseMemberAccess();
                }
                else if (_textParser.CurrentToken.Id == TokenId.NullPropagation)
                {
                    throw new NotSupportedException("An expression tree lambda may not contain a null propagating operator. Use the 'np()' or 'np(...)' (null-propagation) function instead.");
                }
                else if (_textParser.CurrentToken.Id == TokenId.OpenBracket)
                {
                    ParseElementAccess();
                }
                else
                {
                    break;
                }
            }

            if (_textParser.CurrentToken.Id != TokenId.End)
                _primaryStack.Pop();
        }

        private void ParsePrimaryStart()
        {
            switch (_textParser.CurrentToken.Id)
            {
                case TokenId.Identifier:
                    ParseIdentifier();
                    break;

                case TokenId.StringLiteral:
                    ParseStringLiteralAsStringExpressionOrTypeExpression();
                    break;

                case TokenId.IntegerLiteral:
                    ParseIntegerLiteral();
                    break;

                case TokenId.RealLiteral:
                    ParseRealLiteral();
                    break;

                case TokenId.OpenParen:
                    ParseParenExpression();
                    break;

                default:
                    throw ParseError("ExpressionExpected");
            }
        }

        private void ParseStringLiteralAsStringExpressionOrTypeExpression()
        {
            var clonedTextParser = _textParser.Clone();
            clonedTextParser.NextToken();

            // Check if next token is a "(" or a "?(".
            // Used for casting like $"\"System.DateTime\"(Abc)" or $"\"System.DateTime\"?(Abc)".
            // In that case, the string value is NOT forced to stay a string.
            bool forceParseAsString = true;
            if (clonedTextParser.CurrentToken.Id == TokenId.OpenParen)
            {
                forceParseAsString = false;
            }
            else if (clonedTextParser.CurrentToken.Id == TokenId.Question)
            {
                clonedTextParser.NextToken();
                if (clonedTextParser.CurrentToken.Id == TokenId.OpenParen)
                {
                    forceParseAsString = false;
                }
            }

            ParseStringLiteral(forceParseAsString);
        }

        private void ParseStringLiteral(bool forceParseAsString)
        {
            _textParser.ValidateToken(TokenId.StringLiteral);

            var text = _textParser.CurrentToken.Text;
            var parsedStringValue = ParseStringAndEscape(text);

            if (_textParser.CurrentToken.Text[0] == '\'')
            {
                if (parsedStringValue.Length > 1)
                {
                    throw ParseError("InvalidCharacterLiteral");
                }

                _textParser.NextToken();
                return;
            }

            _textParser.NextToken();

            // While the next token is also a string, keep concatenating these strings and get next token
            while (_textParser.CurrentToken.Id == TokenId.StringLiteral)
            {
                text += _textParser.CurrentToken.Text;
                _textParser.NextToken();
            }

            parsedStringValue = ParseStringAndEscape(text);
        }

        private string ParseStringAndEscape(string text)
        {
            return _parsingConfig.StringLiteralParsing == StringLiteralParsingType.EscapeDoubleQuoteByTwoDoubleQuotes ?
                StringParser.ParseStringAndUnescapeTwoDoubleQuotesByASingleDoubleQuote(text, _textParser.CurrentToken.Pos) :
                StringParser.ParseStringAndUnescape(text, _textParser.CurrentToken.Pos);
        }

        private void ParseIntegerLiteral()
        {
            _textParser.ValidateToken(TokenId.IntegerLiteral);
            string text = _textParser.CurrentToken.Text;
            var tokenPosition = _textParser.CurrentToken.Pos;
            _textParser.NextToken();
        }

        private void ParseRealLiteral()
        {
            _textParser.ValidateToken(TokenId.RealLiteral);
            string text = _textParser.CurrentToken.Text;
            _textParser.NextToken();
        }

        private void ParseParenExpression()
        {
            _textParser.ValidateToken(TokenId.OpenParen, "OpenParenExpected");
            _textParser.NextToken();
            ParseConditionalOperator();
            _textParser.ValidateToken(TokenId.CloseParen, "CloseParenOrOperatorExpected");
            _textParser.NextToken();
        }

        private void ParseIdentifier()
        {
            _textParser.ValidateToken(TokenId.Identifier);
            if (TokenIsIdentifier("new"))
                ParseNew();
            else
                ParseMemberAccess();
        }

        // new (...) function
        private void ParseNew()
        {
            _textParser.NextToken();
            if (_textParser.CurrentToken.Id != TokenId.OpenParen &&
                _textParser.CurrentToken.Id != TokenId.OpenCurlyParen &&
                _textParser.CurrentToken.Id != TokenId.OpenBracket &&
                _textParser.CurrentToken.Id != TokenId.Identifier)
            {
                throw ParseError("OpenParenOrIdentifierExpected");
            }

            if (_textParser.CurrentToken.Id == TokenId.Identifier)
            {
                var newTypeName = _textParser.CurrentToken.Text;

                _textParser.NextToken();

                while (_textParser.CurrentToken.Id is TokenId.Dot or TokenId.Plus)
                {
                    var sep = _textParser.CurrentToken.Text;
                    _textParser.NextToken();
                    if (_textParser.CurrentToken.Id != TokenId.Identifier)
                    {
                        throw ParseError("IdentifierExpected");
                    }
                    newTypeName += sep + _textParser.CurrentToken.Text;
                    _textParser.NextToken();
                }

                if (_textParser.CurrentToken.Id != TokenId.OpenParen &&
                    _textParser.CurrentToken.Id != TokenId.OpenBracket &&
                    _textParser.CurrentToken.Id != TokenId.OpenCurlyParen)
                {
                    throw ParseError("OpenParenExpected");
                }
            }

            bool arrayInitializer = false;
            if (_textParser.CurrentToken.Id == TokenId.OpenBracket)
            {
                _textParser.NextToken();
                _textParser.ValidateToken(TokenId.CloseBracket, "CloseBracketExpected");
                _textParser.NextToken();
                _textParser.ValidateToken(TokenId.OpenCurlyParen, "OpenCurlyParenExpected");
                arrayInitializer = true;
            }

            _textParser.NextToken();

            var properties = new List<DynamicProperty>();
            var expressions = new List<Expression>();

            while (_textParser.CurrentToken.Id != TokenId.CloseParen && _textParser.CurrentToken.Id != TokenId.CloseCurlyParen)
            {
                int exprPos = _textParser.CurrentToken.Pos;
                ParseConditionalOperator();
                if (!arrayInitializer)
                {
                    string? propName;
                    if (TokenIsIdentifier("as"))
                    {
                        _textParser.NextToken();
                        propName = GetIdentifierAs();
                    }
                }

                if (_textParser.CurrentToken.Id != TokenId.Comma)
                {
                    break;
                }

                _textParser.NextToken();
            }

            if (_textParser.CurrentToken.Id != TokenId.CloseParen && _textParser.CurrentToken.Id != TokenId.CloseCurlyParen)
            {
                throw ParseError("CloseParenOrCommaExpected");
            }
            _textParser.NextToken();
        }

        private void ParseLambdaInvocation()
        {
            int errorPos = _textParser.CurrentToken.Pos;
            _textParser.NextToken();
            ParseArgumentList();
        }

        private void ParseMemberAccess()
        {
            var errorPos = _textParser.CurrentToken.Pos;
            var id = GetIdentifier();
            _textParser.NextToken();

            // Parse as Lambda
            if (_textParser.CurrentToken.Id == TokenId.Lambda)
            {
                ParseAsLambda();
                return;
            }

            // This could be enum like "A.B.C.MyEnum.Value1" or "A.B.C+MyEnum.Value1".
            //
            // Or it's a nested (static) class with a
            // - static property like "NestedClass.MyProperty"
            // - static method like "NestedClass.MyMethod"
            if (_textParser.CurrentToken.Id is TokenId.Dot or TokenId.Plus)
            {
                ParseAsEnumOrNestedClass();
            }

            if (_textParser.CurrentToken.Id == TokenId.OpenParen)
            {
                ParseArgumentList();
            }
        }

        private void ParseAsLambda()
        {
            // next
            _textParser.NextToken();
            ParseConditionalOperator();
        }

        private void ParseAsEnumOrNestedClass()
        {
            while (_textParser.CurrentToken.Id is TokenId.Dot or TokenId.Plus)
            {
                if (_textParser.CurrentToken.Id is TokenId.Dot or TokenId.Plus)
                {
                    _textParser.NextToken();
                }

                if (_textParser.CurrentToken.Id == TokenId.Identifier)
                {
                    _textParser.NextToken();
                }
            }

            if (_textParser.CurrentToken.Id == TokenId.Identifier)
                ParseMemberAccess();
        }

        private void ParseArgumentList()
        {
            _textParser.ValidateToken(TokenId.OpenParen, "OpenParenExpected");
            _textParser.NextToken();

            if (_textParser.CurrentToken.Id != TokenId.CloseParen)
                ParseArguments();

            _textParser.ValidateToken(TokenId.CloseParen, "CloseParenOrCommaExpected");
            _textParser.NextToken();
        }

        private void ParseArguments()
        {
            while (true)
            {
                ParseOutKeyword();
                if (_textParser.CurrentToken.Id != TokenId.Comma)
                {
                    break;
                }

                _textParser.NextToken();
            }
        }

        private void ParseElementAccess()
        {
            int errorPos = _textParser.CurrentToken.Pos;
            _textParser.ValidateToken(TokenId.OpenBracket, "OpenParenExpected");
            _textParser.NextToken();

            ParseArguments();
            _textParser.ValidateToken(TokenId.CloseBracket, "CloseBracketOrCommaExpected");
            _textParser.NextToken();
        }

        private bool TokenIsIdentifier(string id)
        {
            return _textParser.TokenIsIdentifier(id);
        }

        private string GetIdentifier()
        {
            _textParser.ValidateToken(TokenId.Identifier, "IdentifierExpected");
            return SanitizeId(_textParser.CurrentToken.Text);
        }

        private string GetIdentifierAs()
        {
            _textParser.ValidateToken(TokenId.Identifier, "IdentifierExpected");

            if (!_parsingConfig.SupportDotInPropertyNames)
            {
                var id = SanitizeId(_textParser.CurrentToken.Text);
                _textParser.NextToken();
                return id;
            }

            var parts = new List<string>();
            while (_textParser.CurrentToken.Id is TokenId.Dot or TokenId.Identifier)
            {
                parts.Add(_textParser.CurrentToken.Text);
                _textParser.NextToken();
            }

            return SanitizeId(string.Concat(parts));
        }

        private static string SanitizeId(string id)
        {
            if (id.Length > 1 && id[0] == '@')
            {
                id = id.Substring(1);
            }

            return id;
        }

        private Exception ParseError(string format, params object[] args)
        {
            return ParseError(_textParser.CurrentToken.Pos, format, args);
        }

        private static Exception ParseError(int pos, string format, params object[] args)
        {
            return new ParseException(string.Format(CultureInfo.CurrentCulture, format, args), pos);
        }
    }

    internal static class TokenIdExtensions
    {
        internal static bool IsEqualityOperator(this TokenId tokenId)
        {
            return tokenId is TokenId.Equal or TokenId.DoubleEqual or TokenId.ExclamationEqual or TokenId.LessGreater;
        }

        internal static bool IsComparisonOperator(this TokenId tokenId)
        {
            return tokenId is TokenId.Equal or TokenId.DoubleEqual or TokenId.ExclamationEqual or TokenId.LessGreater or TokenId.GreaterThan or TokenId.GreaterThanEqual or TokenId.LessThan or TokenId.LessThanEqual;
        }
    }

    /// <summary>
    /// Parse a Double and Single Quoted string.
    /// Some parts of the code is based on https://github.com/zzzprojects/Eval-Expression.NET
    /// </summary>
    internal static class StringParser
    {
        private const string TwoDoubleQuotes = "\"\"";
        private const string SingleDoubleQuote = "\"";

        internal static string ParseStringAndUnescape(string s, int pos = default)
        {
            if (s == null || s.Length < 2)
            {
                throw new ParseException(string.Format(CultureInfo.CurrentCulture, "InvalidStringLength", s, 2), pos);
            }

            if (s[0] != '"' && s[0] != '\'')
            {
                throw new ParseException(string.Format(CultureInfo.CurrentCulture, "InvalidStringQuoteCharacter"), pos);
            }

            char quote = s[0]; // This can be single or a double quote
            if (s.Last() != quote)
            {
                throw new ParseException(string.Format(CultureInfo.CurrentCulture, "UnexpectedUnclosedString", s.Length, s), pos);
            }

            try
            {
                return Regex.Unescape(s.Substring(1, s.Length - 2));
            }
            catch (Exception ex)
            {
                throw new ParseException(ex.Message, pos, ex);
            }
        }

        internal static string ParseStringAndUnescapeTwoDoubleQuotesByASingleDoubleQuote(string input, int position = default)
        {
            return ReplaceTwoDoubleQuotesByASingleDoubleQuote(ParseStringAndUnescape(input, position), position);
        }

        private static string ReplaceTwoDoubleQuotesByASingleDoubleQuote(string input, int position)
        {
            try
            {
                return Regex.Replace(input, TwoDoubleQuotes, SingleDoubleQuote);
            }
            catch (Exception ex)
            {
                throw new ParseException(ex.Message, position, ex);
            }
        }
    }
}
