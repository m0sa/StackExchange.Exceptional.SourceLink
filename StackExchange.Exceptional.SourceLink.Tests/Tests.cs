using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Exceptional.SourceLink.Tests
{
    public class Tests : IDisposable
    {
        public Tests(ITestOutputHelper output) =>
            ExceptionalTrace.Init(System.IO.Path.GetDirectoryName(typeof(Full).Assembly.Location), trace: true);

        public void Dispose() => ExceptionalTrace.Shutdown();

        [Theory]
        [InlineData(typeof(Full))]
        [InlineData(typeof(PdbOnly))]
        public void IsSourceLinked(Type exceptionThrower)
        {
            var exception = Assert.Throws<Exception>(() => Activator.CreateInstance(exceptionThrower).ToString());
            var stackTrace = exception.SourceMappedTrace();

            Assert.Contains("//example.org/", stackTrace);
            Assert.Contains("/test1234/", stackTrace);
        }

        [Theory(Skip = "WIP")]
        [InlineData(typeof(Portable))]
        [InlineData(typeof(Embedded))]
        public void IsSourceLinkedFuture(Type exceptionThrower)
        {
            var exception = Assert.Throws<Exception>(() => Activator.CreateInstance(exceptionThrower).ToString());
            var stackTrace = exception.SourceMappedTrace();

            // TODO use dummy sourcelink.json for test projects?
        }
    }
}
