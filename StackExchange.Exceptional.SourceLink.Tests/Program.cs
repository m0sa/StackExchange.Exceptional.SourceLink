using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using StackExchange.Exceptional.Stores;

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

        [Setup]
        public void Setup()
        {
            ExceptionalTrace.Init();
        }

        [Benchmark]
        public string NormalStackTrace()
        {
            try
            {
                throw new Exception("this is a test exception");
            }
            catch (Exception ex)
            {
                return string.Join(Environment.NewLine, new StackTrace(ex));
            }
        }

        [Benchmark]
        public string FancyStackTrace()
        {
            try
            {
                throw new Exception("this is a test exception");
            }
            catch (Exception ex)
            {
                return new StackTrace(ex, true).SourceMappedTrace();
            }
        }
    }
}
