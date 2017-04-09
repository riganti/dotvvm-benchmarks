﻿using System;
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
using BenchmarkDotNet.Diagnostics.Windows;

namespace DotVVM.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            var conf = CreateTestConfiguration();

            var sum1 = BenchmarkRunner.Run<DotvvmSynthTestBenchmark>(conf);

            var sum2 = DotvvmSamplesBenchmarker<Samples.BasicSamples.DotvvmStartup>.BenchmarkSamples(conf);

            //var sum = BenchmarkRunner.Run<Cpu_BranchPerdictor>(conf);
            //BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(config: conf);
        }

        static IConfig CreateTestConfiguration()
        {
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
            conf.Add(new PerfViewBenchmarkDiagnoser("C:/"));
            //conf.Add(new PmcDiagnoser());
            //conf.Add(new BenchmarkDotNet.Diagnostics.Windows.InliningDiagnoser());

            return conf;
        }

        private static Job WithRunCount(Job job)
        {
            job = new Job(job);
            job.Run.WarmupCount = 1;
            return job;
        }
    }

    public class TestViewModel
    {
        public int Property { get; set; }
    }

    //[HardwareCounters(HardwareCounter.BranchMispredictions, HardwareCounter.BranchInstructions, HardwareCounter.LlcMisses, HardwareCounter.CacheMisses)]
    public class DotvvmSynthTestBenchmark
    {
        private DotvvmTestHost host;
        public DotvvmSynthTestBenchmark()
        {
            host = DotvvmTestHost.Create<DotvvmTestHost.DotvvmStartup>();
        }

        [Benchmark]
        public void TestMininalPage()
        {
            var ccc = host.AddFile("<html><head></head><body></body></html>", typeof(object));

            var tt = host.GetRequest(ccc);
            tt.Wait();
        }

        [Benchmark]
        public void Test1000Bindins()
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
}