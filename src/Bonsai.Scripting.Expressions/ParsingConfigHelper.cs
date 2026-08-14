using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.CustomTypeProviders;

namespace Bonsai.Scripting.Expressions
{
    internal static class ParsingConfigHelper
    {
        public static ParsingConfig CreateParsingConfig(params Type[] additionalTypes)
        {
            var config = new ParsingConfig();
            config.CustomTypeProvider = CreateCustomTypeProvider(config, additionalTypes);
            return config;
        }

        static IDynamicLinqCustomTypeProvider CreateCustomTypeProvider(ParsingConfig config, params Type[] additionalTypes)
        {
            return new SimpleDynamicLinqCustomTypeProvider(
                config,
                additionalTypes.SelectMany(EnumerateTypeHierarchy).ToList());
        }

        static IEnumerable<Type> EnumerateTypeHierarchy(Type type)
        {
            var visited = new HashSet<Type>();
            var stack = new Stack<Type>();
            stack.Push(type);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!visited.Add(current)) continue;
                foreach (var interfaceType in current.GetInterfaces())
                {
                    yield return interfaceType;
                }
                for (var baseType = current; baseType is not null; baseType = baseType.BaseType)
                {
                    yield return baseType;
                }
                foreach (var property in current.GetProperties())
                {
                    stack.Push(property.PropertyType);
                }
            }
        }

        class SimpleDynamicLinqCustomTypeProvider : DefaultDynamicLinqCustomTypeProvider
        {
            readonly HashSet<Type> customTypes;

            public SimpleDynamicLinqCustomTypeProvider(ParsingConfig config, IList<Type> additionalTypes)
                : base(config, additionalTypes, cacheCustomTypes: false)
            {
                customTypes = new(AdditionalTypes);
            }

            public override HashSet<Type> GetCustomTypes()
            {
                return customTypes;
            }
        }
    }
}
