using Bonsai.Expressions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Linq;

namespace Bonsai.Scripting.Expressions
{
    /// <summary>
    /// Represents an operator that uses an expression script to generate an
    /// observable sequence with a single element.
    /// </summary>
    [DefaultProperty(nameof(Expression))]
    [WorkflowElementCategory(ElementCategory.Source)]
    [TypeDescriptionProvider(typeof(ExpressionSourceTypeDescriptionProvider))]
    [Description("An expression script used to generate a sequence with a single element.")]
    public class ExpressionSource : ZeroArgumentExpressionBuilder, IScriptingElement
    {
        /// <summary>
        /// Gets or sets the name of the expression source.
        /// </summary>
        [Externalizable(false)]
        [Category(nameof(CategoryAttribute.Design))]
        [Description("The name of the expression source.")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a description for the expression source.
        /// </summary>
        [Externalizable(false)]
        [Category(nameof(CategoryAttribute.Design))]
        [Description("A description for the expression source.")]
        [Editor(DesignTypes.MultilineStringEditor, DesignTypes.UITypeEditor)]
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the expression that generates the singleton element.
        /// </summary>
        /// <remarks>
        /// The <c>it</c> parameter stores the singleton <see cref="Unit"/> object
        /// representing no data.
        /// </remarks>
        [Editor("Bonsai.Scripting.Expressions.Design.ExpressionScriptEditor, Bonsai.Scripting.Expressions.Design", DesignTypes.UITypeEditor)]
        [Description("The expression that generates the singleton element.")]
        public string Expression { get; set; } = "it";

        /// <inheritdoc/>
        public override Expression Build(IEnumerable<Expression> arguments)
        {
            var config = ParsingConfigHelper.CreateParsingConfig(typeof(Unit));
            var generator = DynamicExpressionHelper.ParseLambda(config, typeof(Unit), null, Expression);
            return System.Linq.Expressions.Expression.Call(typeof(ExpressionSource), nameof(Process), new[] { generator.ReturnType }, generator);
        }

        static IObservable<TResult> Process<TResult>(Func<Unit, TResult> generator)
        {
            return Observable.Return(Unit.Default).Select(generator);
        }

        class ExpressionSourceTypeDescriptionProvider : TypeDescriptionProvider
        {
            static readonly TypeDescriptionProvider parentProvider = TypeDescriptor.GetProvider(typeof(ExpressionSource));

            public ExpressionSourceTypeDescriptionProvider()
                : base(parentProvider)
            {
            }

            public override ICustomTypeDescriptor GetExtendedTypeDescriptor(object instance)
            {
                return new ScriptingElementTypeDescriptor(instance,
                    "An expression script used to generate a sequence with a single element.");
            }
        }
    }
}
