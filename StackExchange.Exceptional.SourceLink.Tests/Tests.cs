using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace StackExchange.Exceptional.SourceLink.Tests
{

    public class XUnitTraceListener : TraceListener
    {
        private readonly ITestOutputHelper _output;

        public XUnitTraceListener(ITestOutputHelper output) : base("XUnit")
        {
            _output = output;
        }

        public override void Write(string message)
        {
            _output.WriteLine(message);
        }

        public override void WriteLine(string message)
        {
            _output.WriteLine(message);
        }
    }

    public class ExceptionalTraceFixture : IDisposable
    {
        public ExceptionalTraceFixture()
        {
            ExceptionalTrace.Init(new Uri(System.IO.Path.GetDirectoryName(GetType().Assembly.CodeBase)).LocalPath, trace: true);
        }

        public void Dispose()
        {
            ExceptionalTrace.Shutdown();
        }
    }
    public class Tests : IDisposable, IClassFixture<ExceptionalTraceFixture>
    {
        private readonly TraceListener _output;
        public Tests(ITestOutputHelper output)
        {
            _output = new XUnitTraceListener(output);
            Trace.Listeners.Add(_output);
        }

        public void Dispose()
        {
            Trace.Listeners.Remove(_output);
            _output.Dispose();
        }

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
