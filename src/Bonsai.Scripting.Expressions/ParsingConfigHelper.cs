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
            var interfaces = type.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                yield return interfaces[i];
            }

            while (type is not null)
            {
                yield return type;
                type = type.BaseType;
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
