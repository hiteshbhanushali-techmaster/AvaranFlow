using System;
using System.Collections.Generic;
using System.Reflection;
using ExceptionMaster.Alerting.Enums;
using ExceptionMaster.Alerting.Rules;
using Xunit;

namespace ExceptionMaster.Alerting.Routing.Tests
{
    public class DeclarativeAstBoundaryAndExactNumericTests
    {
        [Fact]
        public void RuleCondition_HasNoPublicOrProtectedConstructors_PreventingExternalInheritance()
        {
            var constructors = typeof(RuleCondition).GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var ctor in constructors)
            {
                Assert.False(ctor.IsPublic, "RuleCondition must not have public constructors.");
                Assert.False(ctor.IsFamily, "RuleCondition must not have protected constructors.");
                Assert.False(ctor.IsFamilyOrAssembly, "RuleCondition must not have protected internal constructors.");
                Assert.True(ctor.IsAssembly, "RuleCondition constructors must be strictly internal.");
            }
        }

        [Fact]
        public void IRuleCondition_IsInternal_NotExportedPublicly()
        {
            var coreAssembly = typeof(RuleCondition).Assembly;
            var publicTypes = coreAssembly.GetExportedTypes();

            Assert.DoesNotContain(publicTypes, t => t.Name == "IRuleCondition");
        }

        [Fact]
        public void ConcreteAstNodeTypes_AreInternal_NotExportedPublicly()
        {
            var coreAssembly = typeof(RuleCondition).Assembly;
            var publicTypes = coreAssembly.GetExportedTypes();

            Assert.DoesNotContain(publicTypes, t => t.Name == "FieldPredicateCondition");
            Assert.DoesNotContain(publicTypes, t => t.Name == "AndCondition");
            Assert.DoesNotContain(publicTypes, t => t.Name == "OrCondition");
            Assert.DoesNotContain(publicTypes, t => t.Name == "NotCondition");
        }

        [Fact]
        public void AlertRule_PublicConstructor_EnforcesRuleConditionType()
        {
            var constructors = typeof(AlertRule).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.Single(constructors);

            var parameters = constructors[0].GetParameters();
            var conditionParam = parameters[2];

            Assert.Equal("condition", conditionParam.Name);
            Assert.Equal(typeof(RuleCondition), conditionParam.ParameterType);
        }

        [Fact]
        public void AllPublicRuleConditionFactoryMethods_ProduceValidDeclarativeNodes()
        {
            var nodes = new List<RuleCondition>
            {
                RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                RuleCondition.ExceptionType(PredicateOperator.Contains, "Timeout"),
                RuleCondition.ExceptionKey(PredicateOperator.StartsWith, "Key"),
                RuleCondition.Severity(PredicateOperator.GreaterThanOrEqual, AlertSeverity.Error),
                RuleCondition.Count(PredicateOperator.GreaterThan, 10),
                RuleCondition.IsFatal(true),
                RuleCondition.CorrelationId(PredicateOperator.Equals, "ORD-123"),
                RuleCondition.EnvironmentName(PredicateOperator.NotEquals, "Dev"),
                RuleCondition.Metadata("OrderId", PredicateOperator.Equals, "12345"),
                RuleCondition.MetadataExists("Symbol"),
                RuleCondition.Field(RuleField.ProcessName, PredicateOperator.EndsWith, "Service"),
                RuleCondition.And(
                    RuleCondition.Category(PredicateOperator.Equals, "Trading"),
                    RuleCondition.Count(PredicateOperator.GreaterThanOrEqual, 5)),
                RuleCondition.Or(
                    RuleCondition.Severity(PredicateOperator.Equals, AlertSeverity.Critical),
                    RuleCondition.IsFatal(true)),
                RuleCondition.Not(RuleCondition.EnvironmentName(PredicateOperator.Equals, "Staging"))
            };

            foreach (var node in nodes)
            {
                var errors = new List<string>();
                node.Validate(errors);
                Assert.Empty(errors);
            }
        }

        [Fact]
        public void IntegralComparisons_AreExact_AndDoNotSufferFromFloatingPointImprecision()
        {
            // Large 64-bit integer where standard IEEE 754 float/double would suffer precision loss
            const long baseCount = 9_007_199_254_740_993L; // 2^53 + 1
            const long nextCount = 9_007_199_254_740_994L;

            var ctx = new RuleEvaluationContext(
                occurrenceId: Guid.NewGuid(),
                exceptionType: "Ex",
                exceptionKey: "Key",
                severity: AlertSeverity.Error,
                category: "Cat",
                source: "Src",
                environmentName: "Env",
                machineName: "Mach",
                processName: "Proc",
                processId: 100,
                threadId: 200,
                appVersion: "1.0",
                isFatal: false,
                count: baseCount,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                correlationId: "Corr",
                caller: "Caller",
                message: "Msg");

            // Exact equality
            Assert.True(RuleCondition.Count(PredicateOperator.Equals, baseCount).Evaluate(ctx));
            Assert.False(RuleCondition.Count(PredicateOperator.Equals, nextCount).Evaluate(ctx));

            // Exact inequality
            Assert.True(RuleCondition.Count(PredicateOperator.NotEquals, nextCount).Evaluate(ctx));
            Assert.False(RuleCondition.Count(PredicateOperator.NotEquals, baseCount).Evaluate(ctx));

            // Exact strictly less than
            Assert.True(RuleCondition.Count(PredicateOperator.LessThan, nextCount).Evaluate(ctx));
            Assert.False(RuleCondition.Count(PredicateOperator.LessThan, baseCount).Evaluate(ctx));

            // Exact greater than or equal
            Assert.True(RuleCondition.Count(PredicateOperator.GreaterThanOrEqual, baseCount).Evaluate(ctx));
            Assert.False(RuleCondition.Count(PredicateOperator.GreaterThanOrEqual, nextCount).Evaluate(ctx));
        }

        [Fact]
        public void ProcessId_And_ThreadId_IntegralComparisons_AreExact()
        {
            var ctx = new RuleEvaluationContext(
                occurrenceId: Guid.NewGuid(),
                exceptionType: "Ex",
                exceptionKey: "Key",
                severity: AlertSeverity.Info,
                category: "Cat",
                source: "Src",
                environmentName: "Env",
                machineName: "Mach",
                processName: "Proc",
                processId: 65432,
                threadId: 88,
                appVersion: "1.0",
                isFatal: false,
                count: 1,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                correlationId: "Corr",
                caller: "Caller",
                message: "Msg");

            Assert.True(RuleCondition.Field(RuleField.ProcessId, PredicateOperator.Equals, 65432).Evaluate(ctx));
            Assert.False(RuleCondition.Field(RuleField.ProcessId, PredicateOperator.Equals, 65431).Evaluate(ctx));

            Assert.True(RuleCondition.Field(RuleField.ThreadId, PredicateOperator.GreaterThan, 80).Evaluate(ctx));
            Assert.False(RuleCondition.Field(RuleField.ThreadId, PredicateOperator.GreaterThan, 88).Evaluate(ctx));
        }
    }
}
