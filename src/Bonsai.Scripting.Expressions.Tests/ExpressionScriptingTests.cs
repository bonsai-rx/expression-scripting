using Bonsai.Expressions;
using System;
using System.Collections;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Bonsai.Scripting.Expressions.Tests
{
    [TestClass]
    public sealed class ExpressionScriptingTests
    {
        async Task AssertExpressionTransform<TSource, TResult>(string expression, TSource value, TResult expected)
        {
            var workflowBuilder = new WorkflowBuilder();
            var source = workflowBuilder.Workflow.Add(new CombinatorBuilder { Combinator = new Return<TSource>(value) });
            var transform = workflowBuilder.Workflow.Add(new ExpressionTransform { Expression = expression });
            var output = workflowBuilder.Workflow.Add(new WorkflowOutputBuilder());
            workflowBuilder.Workflow.AddEdge(source, transform, new());
            workflowBuilder.Workflow.AddEdge(transform, output, new());
            var result = await workflowBuilder.Workflow.BuildObservable<TResult>();
            if (expected is ICollection expectedCollection && result is ICollection resultCollection)
                CollectionAssert.AreEqual(expectedCollection, resultCollection);
            else
                Assert.AreEqual(expected, result);
        }

        [DataTestMethod]
        [DataRow("it", 42, 42)]
        [DataRow("it * 2", 21, 42)]
        [DataRow("Single(it)", 42, 42f)]
        [DataRow("Math.PI", 42, Math.PI)]
        [DataRow("Convert.ToInt16(it)", 42, (short)42)]
        [DataRow("new(it as Data).Data", 42, 42)]
        // modern Dynamic LINQ parser
        [DataRow("float(it)", 42, 42f)]
        [DataRow("long?(it).HasValue", 42, true)]
        [DataRow("bool.TrueString", 42, "True")]
        [DataRow("new[] { it }", 42, new[] { 42 })]
        [DataRow("new[] { it }.Select(x => x * 2).ToArray()", 21, new[] { 42 })]
        [DataRow("np(string(null).Length) ?? it", 42, 42)]
        public Task TestExpressionTransform<TSource, TResult>(string expression, TSource value, TResult expected)
        {
            return AssertExpressionTransform(expression, value, expected);
        }

        [TestMethod]
        public Task TestNullExpression() => AssertExpressionTransform("null", 0, (object)null);

        [TestMethod]
        public Task TestNullString() => AssertExpressionTransform("string(null)", 0, (string)null);

        [TestMethod]
        public Task TestObjectExpression() => AssertExpressionTransform("object(it)", 42, (object)42);

        [DataTestMethod]
        [DataRow("single(it)", 42, 42f)]
        [DataRow("int64?(it).hasvalue", 42, true)]
        [DataRow("math.pi", 42, Math.PI)]
        [DataRow("boolean.truestring", 42, "True")]
        [DataRow("convert.toint16(it)", 42, (short)42)]
        [DataRow("datetime.minvalue.second", 42, 0)]
        [DataRow("datetimeoffset.minvalue.second", 42, 0)]
        [DataRow("guid.empty.tobytearray()[0]", 42, 0)]
        [DataRow("timespan.tickspermillisecond", 0, TimeSpan.TicksPerMillisecond)]
        [DataRow("it > 0 ? convert.toint16(it) : int16.minvalue", 42, (short)42)]
        public Task TestCasingCompatibility<TSource, TResult>(string expression, TSource value, TResult expected)
        {
            return AssertExpressionTransform(expression, value, expected);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("string(it)")]
        public Task TestInvalidExpression(string expression)
        {
            return Assert.ThrowsExactlyAsync<WorkflowBuildException>(() =>
                AssertExpressionTransform(expression, 42, (object)null));
        }

        class Return<TValue>(TValue value) : Source<TValue>
        {
            public TValue Value { get; } = value;

            public override IObservable<TValue> Generate() => Observable.Return(Value);
        }
    }
}
