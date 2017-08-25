using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;

namespace StackExchange.Exceptional.SourceLink.Tests
{
    public class Program
    {
        public static void Main()
        {
            Console.WriteLine(BenchmarkRunner.Run<TraceDumpTest>());
        }
    }

    [Config(typeof(Config))]
    public class TraceDumpTest
    {
        class Config : ManualConfig
        {
            public Config()
            {
                Add(new MemoryDiagnoser());
            }
        }

        [GlobalSetup]
        public void GlobalSetup()
        {
            _throw = Activator.CreateInstance(ExceptionThrowingType).ToString;
            ExceptionalTrace.Init();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            ExceptionalTrace.Shutdown();
        }

        [Params(typeof(Full), typeof(PdbOnly), typeof(Portable), typeof(Embedded))]
        public Type ExceptionThrowingType { get; set; }

        private Func<string> _throw;

        [Benchmark(Baseline = true)]
        public string NormalStackTrace()
        {
            try
            {
                return _throw.Invoke();
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }

        [Benchmark]
        public string FancyStackTrace()
        {
            try
            {
                return _throw.Invoke();
            }
            catch (Exception ex)
            {
                return new StackTrace(ex, true).SourceMappedTrace();
            }
        }
    }
}
