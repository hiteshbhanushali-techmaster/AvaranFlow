# ExceptionMaster — Comprehensive Automated Test Plan

## 1. Test Project Structure

1. **`AvaranFlow.Core.Tests`**: Unit tests for API, metadata snapshotting, aggregation, buffer overflow, and lifecycle.
2. **`AvaranFlow.Sinks.Tests`**: Unit and component tests for Console, File, Database, and NLog sinks.
3. **`AvaranFlow.Concurrency.Tests`**: Parallel logging, multi-threaded aggregation accuracy, concurrent reconfiguration, and atomic counters under load.
4. **`AvaranFlow.FailureInjection.Tests`**: Throwing sinks, database disconnects, locked files, full disks, slow subscribers, and cyclic event recursion.
5. **`AvaranFlow.Integration.Tests`**: End-to-end multi-sink routing, database schema deployment, LocalDB verification, and consumer package tests.

---

## 2. Test Suite Matrix

### Suite A: Core API & Safe Defaults
- `Log_WithNull_DoesNotThrow`
- `Log_BeforeConfigure_UsesSafeDefaults`
- `ConfigureDefaults_RestoresExpectedDefaults`
- `Configure_ThreadSafetyUnderConcurrentLogs`
- `Shutdown_DrainsBuffersWithinTimeout`
- `Shutdown_CalledMultipleTimes_IsSafe`

### Suite B: Metadata & Context
- `ExceptionContext_PreservesOriginalException`
- `ExceptionContext_CapturesAllEnvironmentMetadata`
- `ExceptionContext_IsFatal_PreservesCallerValue`
- `ExceptionContext_UtcTimestamps_AreAccurate`
- `ExceptionContext_InnerExceptions_FormattedRecursively`

### Suite C: Aggregation & Concurrency
- `Aggregation_FirstOccurrence_CountIsOne`
- `Aggregation_RepeatedOccurrence_IncrementsCountAndUpdatesLastSeen`
- `Aggregation_Parallel100Threads_AccurateTotalCount`
- `Aggregation_DifferentExceptions_DoNotCollide`
- `Aggregation_MaxCapacity_EvictsOldestSafely`

### Suite D: Fallback & Fan-Out
- `FanOut_AllSinksInvokedIndependently`
- `FanOut_OneSinkThrows_OtherSinksStillReceiveEvent`
- `Fallback_PrimaryFails_InvokesFirstFallback`
- `Fallback_PrimaryAndFirstFallbackFail_InvokesSecondFallback`
- `Fallback_DoesNotCreateDuplicateDeliveryInSharedSinks`

### Suite E: Fire-and-Forget Event & Recursion Isolation
- `ExceptionRaised_SubscriberInvokedAsynchronously`
- `ExceptionRaised_SlowSubscriber_DoesNotBlockLogger`
- `ExceptionRaised_ThrowingSubscriber_IsolatedFromOtherSubscribers`
- `ExceptionRaised_SubscriberLogsNewException_DoesNotRecurseIndefinitely`
- `ExceptionRaised_UnrelatedConcurrentLogs_NotSuppressed`

### Suite F: Sinks (Console, File, Database, NLog)
- `ConsoleSink_NoConsoleAttached_SwallowsSafely`
- `FileSink_CreatesDirectoryAndFile`
- `FileSink_RotatesWhenSizeExceeded`
- `FileSink_RetainsMaxArchiveFiles`
- `FileSink_FileLocked_RetriesAndIsolatesFailure`
- `DatabaseSink_ValidConnection_InsertsRecord`
- `DatabaseSink_MissingConnectionString_DisablesSafely`
- `DatabaseSink_DbOffline_TimesOutAndTriggersFallback`
- `DatabaseSink_ParametersAreEscaped`
- `NLogSink_RelaysToHostLogger_DoesNotAlterHostConfig`
