using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using AvaranFlow.Alerting.Enums;
using AvaranFlow.Alerting.Generations;
using AvaranFlow.Core;
using Xunit;

namespace AvaranFlow.Alerting.Core.Tests
{
    public class GenerationContractTests
    {
        private static ExceptionContext CreateSampleContext()
        {
            return new ExceptionContext(
                exception: new InvalidOperationException("Generation test"),
                message: "Test message",
                exceptionType: "InvalidOperationException",
                stackTrace: string.Empty,
                firstSeenUtc: DateTime.UtcNow,
                lastSeenUtc: DateTime.UtcNow,
                count: 1,
                caller: "Caller",
                category: "GenCategory",
                machineName: "Mach",
                processName: "Proc",
                processId: 1,
                threadId: 1,
                appVersion: "1.0",
                correlationId: "CORR-GEN",
                isFatal: false,
                exceptionKey: "Key-Gen-1",
                environmentName: "Test",
                source: "Source",
                innerExceptionDetails: string.Empty,
                occurrenceId: Guid.NewGuid());
        }

        [Fact]
        public void AdmittedEvent_EnforcesPositiveGenerationId()
        {
            var ctx = CreateSampleContext();
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdmittedEvent(0, ctx, AlertSeverity.Error));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdmittedEvent(-5, ctx, AlertSeverity.Error));
        }

        [Fact]
        public void AdmittedEvent_SingleClaimGuarantee_UnderConcurrency()
        {
            var ctx = CreateSampleContext();
            var admittedEvent = new AdmittedEvent(generationId: 42, ctx, AlertSeverity.Critical);

            Assert.Equal(42, admittedEvent.GenerationId);
            Assert.False(admittedEvent.IsClaimed);

            const int workerCount = 50;
            var results = new ConcurrentBag<bool>();

            // 50 concurrent tasks all race to claim the same admitted event
            Parallel.For(0, workerCount, _ =>
            {
                bool claimed = admittedEvent.TryClaimForProcessing();
                results.Add(claimed);
            });

            // Exactly ONE worker must have successfully claimed it
            int trueCount = results.Count(r => r);
            int falseCount = results.Count(r => !r);

            Assert.Equal(1, trueCount);
            Assert.Equal(workerCount - 1, falseCount);
            Assert.True(admittedEvent.IsClaimed);

            // Any subsequent sequential claim also fails
            Assert.False(admittedEvent.TryClaimForProcessing());
        }

        [Fact]
        public void GenerationState_Enum_HasDefinedLifecycleStates()
        {
            Assert.True(Enum.IsDefined(typeof(GenerationState), GenerationState.Active));
            Assert.True(Enum.IsDefined(typeof(GenerationState), GenerationState.Draining));
            Assert.True(Enum.IsDefined(typeof(GenerationState), GenerationState.Disposed));
        }
    }
}
