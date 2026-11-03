using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Rules;
using AvaranFlow.Core;
using Xunit;

namespace AvaranFlow.Alerting.Routing.Tests
{
    public class AdversarialSecurityTests
    {
        [Fact]
        public void RuleEvaluationContext_NeverExposesRawExceptionOrUnprojectedObjects()
        {
            var properties = typeof(RuleEvaluationContext).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var propertyTypes = properties.Select(p => p.PropertyType).ToList();

            Assert.DoesNotContain(typeof(Exception), propertyTypes);
            Assert.DoesNotContain(propertyTypes, t => t.FullName != null && t.FullName.Contains("ExceptionContext"));
            Assert.DoesNotContain(typeof(System.Collections.IDictionary), propertyTypes);
        }

        [Fact]
        public void PredicateCondition_ContainsNoDelegateOrExpressionCompilationFields()
        {
            var fields = typeof(FieldPredicateCondition).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                Assert.False(typeof(Delegate).IsAssignableFrom(field.FieldType), $"Condition contains executable delegate field: {field.Name}");
                Assert.False(typeof(MethodInfo).IsAssignableFrom(field.FieldType), $"Condition contains reflection MethodInfo field: {field.Name}");
                Assert.DoesNotContain("Expression", field.FieldType.Name);
            }
        }

        [Fact]
        public void Metadata_IsSnapshotIsolated_FromOriginalExceptionDataMutation()
        {
            var ex = new Exception("Test");
            ex.Data["Token"] = "VALID_TOKEN_123";

            var ctx = new ExceptionContext(
                exception: ex,
                message: "Msg",
                exceptionType: "Type",
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "Caller",
                category: "Trading",
                machineName: "Mach",
                processName: "Proc",
                processId: 1,
                threadId: 1,
                appVersion: "1.0",
                correlationId: "CORR",
                isFatal: false,
                exceptionKey: "Key",
                environmentName: "Prod",
                source: "Src",
                innerExceptionDetails: string.Empty,
                occurrenceId: Guid.NewGuid());

            var evalContext = RuleEvaluationContext.FromExceptionContext(ctx, AlertSeverity.Error);

            // Adversary mutates the original Exception.Data after context extraction
            ex.Data["Token"] = "MALICIOUS_OVERWRITE";
            ex.Data["NewSensitiveField"] = "PRIVATE_DATA";

            // Verify evaluation context was not affected by subsequent mutation of Exception.Data
            var originalCheck = RuleCondition.Metadata("Token", PredicateOperator.Equals, "VALID_TOKEN_123");
            var tamperedCheck = RuleCondition.Metadata("Token", PredicateOperator.Equals, "MALICIOUS_OVERWRITE");
            var leakCheck = RuleCondition.MetadataExists("NewSensitiveField");

            Assert.True(originalCheck.Evaluate(evalContext));
            Assert.False(tamperedCheck.Evaluate(evalContext));
            Assert.False(leakCheck.Evaluate(evalContext));
        }

        [Fact]
        public void CoreAssembly_HasZeroScriptingOrDynamicCompilerReferences()
        {
            var coreAssembly = typeof(RuleEngine).Assembly;
            var referenced = coreAssembly.GetReferencedAssemblies();

            foreach (var refAsm in referenced)
            {
                Assert.DoesNotContain("CodeAnalysis", refAsm.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Scripting", refAsm.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Dynamic", refAsm.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Emit", refAsm.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
