using System;
using System.Linq;
using System.Reflection;
using ExceptionMaster.Alerting.Models;
using Xunit;

namespace ExceptionMaster.Alerting.Core.Tests
{
    public class FrameworkNeutralContractTests
    {
        [Fact]
        public void AlertingCoreAssembly_HasNoUiDependencies()
        {
            var coreAssembly = typeof(AlertEnvelope).Assembly;
            var referencedAssemblies = coreAssembly.GetReferencedAssemblies();

            foreach (var assemblyName in referencedAssemblies)
            {
                Assert.DoesNotContain("Windows.Forms", assemblyName.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Drawing", assemblyName.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("PresentationFramework", assemblyName.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void AllPublicTypes_InCore_AreInExceptionMasterAlertingNamespace()
        {
            var coreAssembly = typeof(AlertEnvelope).Assembly;
            var publicTypes = coreAssembly.GetExportedTypes();

            Assert.NotEmpty(publicTypes);
            foreach (var type in publicTypes)
            {
                Assert.NotNull(type.Namespace);
                Assert.StartsWith("ExceptionMaster.Alerting", type.Namespace);
            }
        }
    }
}
