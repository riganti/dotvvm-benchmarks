using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Attributes;
using System.Collections.Concurrent;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Parameters;
using DotVVM.Framework.Configuration;
using BenchmarkDotNet.Reports;
using System.IO;

namespace DotVVM.Benchmarks
{
    class Program
    {


        static void Main(string[] args)
        {
            new DotvvmSynthTestBenchmark().Test123();

            var conf = ManualConfig.Create(DefaultConfig.Instance);
            conf.Add(BenchmarkDotNet.Exporters.AsciiDocExporter.Default);
            conf.Add(BenchmarkDotNet.Exporters.MarkdownExporter.Default);
            conf.Add(BenchmarkDotNet.Exporters.HtmlExporter.Default);
            conf.Add(BenchmarkDotNet.Exporters.DefaultExporters.JsonFull);
            conf.Add(WithRunCount(Job.RyuJitX64.WithGcServer(true)));
            conf.Add(WithRunCount(Job.RyuJitX64.WithGcServer(false)));
            conf.Add(WithRunCount(Job.Clr.WithGcServer(true)));
            conf.Add(WithRunCount(Job.Clr.WithGcServer(false)));
            conf.Add(BenchmarkDotNet.Columns.StatisticColumn.Min);
            conf.Add(BenchmarkDotNet.Columns.StatisticColumn.Mean);
            conf.Add(BenchmarkDotNet.Columns.StatisticColumn.AllStatistics);
            conf.Add(BenchmarkDotNet.Diagnosers.MemoryDiagnoser.Default);
            conf.Add(new EtwLogDiagnoser("E:/"));
            //conf.Add(new BenchmarkDotNet.Diagnostics.Windows.InliningDiagnoser());

            var sum = BenchmarkSamples(conf);
            //BenchmarkDotNet.Exporters.RPlotExporter.Default.ExportToFiles(sum, BenchmarkDotNet.Loggers.ConsoleLogger.Default);

            //var sum = BenchmarkRunner.Run<Cpu_BranchPerdictor>(conf);
            //BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(config: conf);
        }

        private static Job WithRunCount(Job job)
        {
            job = new Job(job);
            job.Run.WarmupCount = 1;
            return job;
        }

        private static Summary BenchmarkSamples(IConfig config)
        {
            var host = CreateSamplesTestHost();
            var benchmarks = GetAllBenchmarks(config, host).Take(3).ToArray();

            return BenchmarkRunner.Run(benchmarks, config);
        }

        public static DotvvmTestHost CreateSamplesTestHost()
        {
            var currentPath = Directory.GetCurrentDirectory();
            while (Path.GetFileName(currentPath) != "DotVVM.Benchmarks") currentPath = Path.GetDirectoryName(currentPath);
            currentPath = Path.Combine(Path.GetDirectoryName(currentPath), "dotvvm/src/DotVVM.Samples.Common");
            return DotvvmTestHost.Create<Samples.BasicSamples.DotvvmStartup>(currentPath);
        }

        private static IEnumerable<Benchmark> GetAllBenchmarks(IConfig config, DotvvmTestHost testHost)
        {
            return BenchmarkConverter.TypeToBenchmarks(typeof(DotvvmSamplesBenchmarks), config)
                .SelectMany(b => CreateBenchmarks(b, testHost, testHost.Configuration));
        }

        private static IEnumerable<Benchmark> CreateBenchmarks(Benchmark b, DotvvmTestHost host, DotvvmConfiguration config)
        {
            var requests = config.RouteTable.Select(r => new DotvvmSamplesBenchmarks.RequestInfo {
                RouteName = r.RouteName,
                RouteParams = r.ParameterNames.ToDictionary(i => i, e => r.DefaultValues.TryGetValue(e, out var obj) ? obj : "5"),
                TestHost = host
            }).Where(r => r.RouteParams.Count == 0).Select(r => r.RouteName);
            var definiton = new ParameterDefinition(nameof(DotvvmSamplesBenchmarks.RouteInfo), false, new DotvvmSamplesBenchmarks.RequestInfo[0]);
            foreach (var value in requests)
            {
                yield return new Benchmark(b.Target, b.Job, new ParameterInstances(new[] { new ParameterInstance(definiton, value) }));
            }
        }
    }

    //[HardwareCounters(HardwareCounter.BranchMispredictions, HardwareCounter.BranchInstructions, HardwareCounter.LlcMisses)]
    public class DotvvmSamplesBenchmarks
    {
        public class RequestInfo
        {
            public string RouteName { get; set; }
            public Dictionary<string, object> RouteParams { get; set; }
            public DotvvmTestHost TestHost { get; set; }
        }

        private DotvvmTestHost host = Program.CreateSamplesTestHost();

        public string RouteInfo { get; set; }

        [Benchmark]
        public void TestDotvvmRequest()
        {
            var url = "/" + host.Configuration.RouteTable[RouteInfo].BuildUrl().TrimStart('~', '/');
            var r = host.GetRequest(url).Result;
        }
    }

    public class Configuration : ManualConfig
    {
        public Configuration()
        {
            Add(Job.RyuJitX64.WithGcServer(true));
            Add(Job.RyuJitX64.WithGcServer(false));
        }
    }

    public class TestBenchmarkClass
    {

    }

    public class TestViewModel
    {
        public int Property { get; set; }
    }

    public class DotvvmSynthTestBenchmark
    {
        private DotvvmTestHost host;
        public DotvvmSynthTestBenchmark()
        {
            host = DotvvmTestHost.Create<DotvvmTestHost.DotvvmStartup>();
        }

        [Benchmark]
        public void Test123()
        {
            var literal = "{{value: Property}} ";
            var ccc = host.AddFile($@"
<html>
<head>
</head>
<body>
<div>
    {string.Concat(Enumerable.Repeat(literal, 1000))}
</div>
</body>
</html>
", typeof(TestViewModel));

            var tt = host.GetRequest(ccc);
            tt.Wait();
        }
    }

    //[HardwareCounters(HardwareCounter.BranchMispredictions, HardwareCounter.BranchInstructions, HardwareCounter.LlcMisses)]
    public class Cpu_BranchPerdictor
    {
        private const int N = 32767;
        private readonly int[] sorted, unsorted;

        public Cpu_BranchPerdictor()
        {
            var random = new Random(0);
            unsorted = new int[N];
            sorted = new int[N];
            for (int i = 0; i < N; i++)
                sorted[i] = unsorted[i] = random.Next(256);
            Array.Sort(sorted);
        }

        private static int Branch(int[] data)
        {
            int sum = 0;
            for (int i = 0; i < N; i++)
                if (data[i] >= 128)
                    sum += data[i];
            return sum;
        }

        private static int Branchless(int[] data)
        {
            int sum = 0;
            for (int i = 0; i < N; i++)
            {
                int t = (data[i] - 128) >> 31;
                sum += ~t & data[i];
            }
            return sum;
        }

        [Benchmark]
        public int SortedBranch() => Branch(sorted);

        [Benchmark]
        public int UnsortedBranch() => Branch(unsorted);

        [Benchmark]
        public int SortedBranchless() => Branchless(sorted);

        [Benchmark]
        public int UnsortedBranchless() => Branchless(unsorted);
    }
}
