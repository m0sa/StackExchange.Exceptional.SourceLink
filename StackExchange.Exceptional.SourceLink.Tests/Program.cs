using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using StackExchange.Exceptional.Stores;
using StackMail.Client;
using StackMail.Operations;

namespace StackExchange.Exceptional.SourceLink.Tests
{
    public class Program
    {
        public static void Main()
        {
            Console.WriteLine(BenchmarkRunner.Run<TraceDumpTest>());
        }
    }
    

    public class TraceDumpTest
    {
        private StackMailClient _client;
        private ImmediatelySendEmailRequest _request;

        [Setup]
        public void Setup()
        {
            ExceptionalTrace.Init();

            _client = new StackMailClient(); // this is from a known, SRCSRV mapped assembly
            _request = new ImmediatelySendEmailRequest { AccountId = 42 };
        }

        [Benchmark]
        public string NormalStackTrace()
        {
            try
            {
                return _client.SendEmailAsync(_request).ConfigureAwait(false).GetAwaiter().GetResult()?.RawResponse;
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
                return _client.SendEmailAsync(_request).ConfigureAwait(false).GetAwaiter().GetResult()?.RawResponse;
            }
            catch (Exception ex)
            {
                return new StackTrace(ex, true).FancyTrace();
            }
        }
    }
}
