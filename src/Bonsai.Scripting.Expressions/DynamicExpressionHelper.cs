using System;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Exceptions;
using System.Linq.Expressions;

namespace Bonsai.Scripting.Expressions
{
    internal class DynamicExpressionHelper
    {
        public static LambdaExpression ParseLambda(Type delegateType, ParsingConfig? parsingConfig, ParameterExpression[] parameters, Type? resultType, string expression, params object?[] values)
        {
            return ParseLambda(delegateType, parsingConfig, true, parameters, resultType, expression, values);
        }

        public static LambdaExpression ParseLambda(ParsingConfig? parsingConfig, Type itType, Type? resultType, string expression, params object?[] values)
        {
            return ParseLambda(null, parsingConfig, true, new[] { Expression.Parameter(itType, "it") }, resultType, expression, values);
        }

        public static LambdaExpression ParseLambda(Type? delegateType, ParsingConfig? parsingConfig, bool createParameterCtor, ParameterExpression[] parameters, Type? resultType, string expression, params object?[] values)
        {
            try
            {
                return DynamicExpressionParser.ParseLambda(delegateType, parsingConfig, createParameterCtor, parameters, resultType, expression, values);
            }
            catch (ParseException)
            {
                if (!CompatibilityAnalyzer.ReplaceLegacyKeywords(parsingConfig, expression, out expression))
                    throw;

                return DynamicExpressionParser.ParseLambda(delegateType, parsingConfig, createParameterCtor, parameters, resultType, expression, values);
            }
        }
    }
}
