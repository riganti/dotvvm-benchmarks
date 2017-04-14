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
using BenchmarkDotNet.Diagnostics.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

#if C_77b3b6f || DEBUG
#else
#error You should not build the solution by yourself (except for debugging purposes), use BenchmarkRunner
#endif


namespace DotVVM.Benchmarks
{
    public class DotvvmSamplesLauncher : IApplicationLauncher
    {
        public DotvvmConfiguration ConfigApp(IApplicationBuilder app, string currentPath)
        {
            return app.UseDotVVM<DotVVM.Samples.BasicSamples.DotvvmStartup>(Path.Combine(currentPath, "dotvvm/src/DotVVM.Samples.Common"));
        }

        public void ConfigServices(IServiceCollection services)
        {
            services.AddDotVVM();
        }
    }

    public class DotvvmPerfTestsLauncher : IApplicationLauncher
    {
        public DotvvmConfiguration ConfigApp(IApplicationBuilder app, string currentPath)
        {
            return app.UseDotVVM<WebApp.DotvvmStartup>(Path.Combine(currentPath, "DotVVM.Benchmarks/WebApp"));
        }

        public void ConfigServices(IServiceCollection services)
        {
            services.AddDotVVM();
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var conf = CreateTestConfiguration();

            //var sum1 = BenchmarkRunner.Run<DotvvmSynthTestBenchmark>(conf);

            //var sum2 = DotvvmSamplesBenchmarker<DotvvmSamplesLauncher>.BenchmarkSamples(conf, postRequests: true, getRequests: true);

            var sum3 = DotvvmSamplesBenchmarker<DotvvmPerfTestsLauncher>.BenchmarkSamples(conf, getRequests: true, postRequests: true);

            //var sum = BenchmarkRunner.Run<Cpu_BranchPerdictor>(conf);
            //BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(config: conf);
#if DEBUG
            Console.ReadLine();
#endif
        }

        static IConfig CreateTestConfiguration()
        {
            var conf = ManualConfig.Create(DefaultConfig.Instance);
            conf.Add(BenchmarkDotNet.Exporters.AsciiDocExporter.Default);
            conf.Add(BenchmarkDotNet.Exporters.MarkdownExporter.Default);
            conf.Add(BenchmarkDotNet.Exporters.HtmlExporter.Default);
            conf.Add(BenchmarkDotNet.Exporters.DefaultExporters.JsonFull);
            conf.Add(WithRunCount(Job.RyuJitX64.WithGcServer(true)));
            //conf.Add(WithRunCount(Job.RyuJitX64.WithGcServer(false)));
            //conf.Add(WithRunCount(Job.Clr.WithGcServer(true)));
            //conf.Add(WithRunCount(Job.Clr.WithGcServer(false)));
            conf.Add(BenchmarkDotNet.Columns.StatisticColumn.Min);
            conf.Add(BenchmarkDotNet.Columns.StatisticColumn.OperationsPerSecond);


            //conf.Add(BenchmarkDotNet.Columns.StatisticColumn.Mean);
            //conf.Add(BenchmarkDotNet.Columns.StatisticColumn.AllStatistics);
            conf.Add(BenchmarkDotNet.Diagnosers.MemoryDiagnoser.Default);
            conf.Add(new PerfViewBenchmarkDiagnoser("C:/", 
                methodColumns: new[] {
                        ("DotVVM.Framework!DotVVM.Framework.Hosting.DotvvmPresenter.ProcessRequest(class DotVVM.Framework.Hosting.IDotvvmRequestContext)", "ProcessRequest"),
                        ("DotVVM.Framework!DotVVM.Framework.Runtime.DefaultOutputRenderer.WriteHtmlResponse(class DotVVM.Framework.Hosting.IDotvvmRequestContext,class DotVVM.Framework.Controls.Infrastructure.DotvvmView)", "WriteHtmlResponse"),
                        ("DotVVM.Framework!DotVVM.Framework.Runtime.DefaultDotvvmViewBuilder.BuildView(class DotVVM.Framework.Hosting.IDotvvmRequestContext)", "BuildView"),
                        ("DotVVM.Framework!DotVVM.Framework.Controls.DotvvmBindableObject.GetValue(class DotVVM.Framework.Binding.DotvvmProperty,bool)", "BindableObject.GetValue"),
                        ("DotVVM.Framework!DotVVM.Framework.Controls.DotvvmControlCollection.InvokePageLifecycleEventRecursive(class DotVVM.Framework.Controls.DotvvmControls,value class DotVVM.Framework.Controls.LifeCycleEventType)", "Lifecycle events"),
                        ("DotVVM.Framework!DotVVM.Framework.ViewModel.Serialization.DefaultViewModelSerializer.PopulateViewModel(class DotVVM.Framework.Hosting.IDotvvmRequestContext,class System.String)", "Deserialize"),
                        ("DotVVM.Framework!DotVVM.Framework.ViewModel.Serialization.DefaultViewModelSerializer.ResolveCommand(class DotVVM.Framework.Hosting.IDotvvmRequestContext,class DotVVM.Framework.Controls.Infrastructure.DotvvmView)", "ResolveCommand"),
                        ("DotVVM.Framework!DotVVM.Framework.ViewModel.Serialization.DefaultViewModelSerializer.BuildViewModel(class DotVVM.Framework.Hosting.IDotvvmRequestContext)", "Serialize"),
                    }));
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
            host = DotvvmTestHost.Create<DotvvmTestHost.DefaultLauncher>();
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
