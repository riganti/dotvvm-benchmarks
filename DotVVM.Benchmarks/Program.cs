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
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using BenchmarkDotNet.Characteristics;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.Results;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Horology;
using BenchmarkDotNet.Toolchains.Parameters;

// #if C_77b3b6f || DEBUG
// #else
// #error You should not build the solution by yourself (except for debugging purposes), use BenchmarkRunner
// #endif


namespace DotVVM.Benchmarks
{

    public class DotvvmSamplesLauncher : IApplicationLauncher
    {
        public DotvvmConfiguration ConfigApp(IApplicationBuilder app, string currentPath)
        {
            return app.UseDotVVM<DotVVM.Samples.BasicSamples.DotvvmStartup>(Path.Combine(currentPath, "dotvvm/src/DotVVM.Samples.Common"));
        }

        public void ConfigServices(IServiceCollection services, string currentPath)
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

        public void ConfigServices(IServiceCollection services, string currentPath)
        {
            services.AddDotVVM();
        }
    }

    public class SynchonousBuilder: IBuilder
    {
        private IBuilder builder;
        private object syncLock = new object();

        public SynchonousBuilder(IBuilder builder)
        {
            this.builder = builder;
        }

        public BuildResult Build(GenerateResult generateResult, BuildPartition buildPartition, ILogger logger)
        {
            lock(syncLock)
            {
                return builder.Build(generateResult, buildPartition, logger);
            }
        }
    }

    public class InterceptingExecutor: IExecutor
    {
        public static ExecuteResult LastExecResult { get; private set; }

        private readonly IExecutor executor;

        public InterceptingExecutor(IExecutor executor)
        {
            this.executor = executor;
        }

        public ExecuteResult Execute(ExecuteParameters executeParameters)
        {
            return LastExecResult = this.executor.Execute(executeParameters);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
#if RUN_frontend_tests
            FrontendBenchmarker.BenchmarkApplication<DotvvmSamplesLauncher>(new BrowserTimeOptions { }, ".");
            return;
#endif

            Environment.SetEnvironmentVariable("COMPlus_PerfMapEnabled", "1");
            if (Directory.Exists("testViewModels")) Directory.Delete("testViewModels", true);

            var conf = CreateTestConfiguration();

            var b = new List<BenchmarkRunInfo>();
#if RUN_synth_tests
            b.Add(BenchmarkConverter.TypeToBenchmarks(typeof(Benchmarks.RequestBenchmarks), conf));
            b.Add(BenchmarkConverter.TypeToBenchmarks(typeof(Benchmarks.ParserBenchmarks), conf));
#endif
            // b.Add(BenchmarkConverter.TypeToBenchmarks(typeof(Benchmarks.SingleControlTests), conf));
#if RUN_dotvvm_samples
            b.AddRange(DotvvmSamplesBenchmarker<DotvvmSamplesLauncher>.BenchmarkSamples(conf, postRequests: true, getRequests: true));
#endif
#if RUN_perf_samples
            b.AddRange(DotvvmSamplesBenchmarker<DotvvmPerfTestsLauncher>.BenchmarkSamples(conf, getRequests: true, postRequests: false));
            Console.WriteLine("Running with perf samples");
#endif
#if RUN_aspnet_mvc
            b.AddRange(DotvvmSamplesBenchmarker<MvcWebApp.MvcAppLauncher>.BenchmarkMvcSamples(conf));
#endif

            BenchmarkRunner.Run(
                //b.GroupBy(t => t.Parameters.Items.Any(p => p.Name == nameof(DotvvmPostbackBenchmarks<DotvvmSamplesLauncher>.SerializedViewModel))).SelectMany(g => g.Take(27).Skip(25))
                b
                //b.Take(1)
                .ToArray(), conf);
        }

        static IConfig CreateTestConfiguration()
        {
            var methodColumns = new[] {
                                    ("DotVVM.Framework.Hosting.DotvvmPresenter::ProcessRequest", "ProcessRequest"),
                                    ("DotVVM.Framework.Runtime.DefaultOutputRenderer::WriteHtmlResponse", "WriteHtmlResponse"),
                                    ("DotVVM.Framework.Runtime.DefaultDotvvmViewBuilder::BuildView", "BuildView"),
                                    ("DotVVM.Framework.Controls.DotvvmBindableObject::GetValue", "BindableObject.GetValue"),
                                    ("DotVVM.Framework.Controls.DotvvmControlCollection::InvokeMissedPageLifeCycleEvent", "Lifecycle events"),
                                    ("DotVVM.Framework.ViewModel.Serialization.DefaultViewModelSerializer::PopulateViewModel", "Deserialize"),
                                    ("DotVVM.Framework.ViewModel.Serialization.DefaultViewModelSerializer::ResolveCommand", "ResolveCommand"),
                                    ("DotVVM.Framework.ViewModel.Serialization.DefaultViewModelSerializer::BuildViewModel", "Serialize"),
                                };
            var conf = ManualConfig.Create(DefaultConfig.Instance);
            conf.Add(BenchmarkDotNet.Exporters.MarkdownExporter.Default);
            conf.Add(BenchmarkDotNet.Exporters.HtmlExporter.Default);
            conf.Add(new MyJsonExporter());
            conf.Add(WithRunCount(Job.RyuJitX64.WithGcServer(true).WithGcForce(false)));
            conf.Add(BenchmarkDotNet.Columns.StatisticColumn.Median);
            conf.Add(BenchmarkDotNet.Columns.StatisticColumn.OperationsPerSecond);

            conf.Add(new CpuTimeDiagnoser());
#if DIAGNOSER_cpu_sampling
            var benchmarkDiagnoser = new LinuxPerfBenchmarkDiagnoser(methodColumns: methodColumns, enableStacksExport: true,
#if DEBUG || EXPORT_rawperf
                enableRawPerfExport: true
#else
                enableRawPerfExport: false
#endif
            );
            conf.Add(benchmarkDiagnoser);
            benchmarkDiagnoser.AddColumnsToConfig(conf);
            Console.WriteLine("CPU Sampling [ON]");
            conf.Add(SynchronousMemoryDiagnoser.Default);
#endif
            return conf;
        }

        private static Job WithRunCount(Job job)
        {
            job = new Job(job);
            var toolchain = BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.Current.Value;
            job.Infrastructure.Toolchain = new Toolchain(toolchain.Name, toolchain.Generator, new SynchonousBuilder(toolchain.Builder), new InterceptingExecutor(toolchain.Executor));
            // job = job.WithMinIterationTime(BenchmarkDotNet.Horology.TimeInterval.FromMilliseconds(200));
            //job.Run.WarmupCount = 1;
            return job
                .With(new MsBuildArgument[] { new MsBuildArgument("--disable-parallel") })
                .WithMaxRelativeError(0.008)
#if DEBUG_RUN
                .WithMaxIterationCount(8).WithMinIterationCount(6).WithWarmupCount(1)
#else
                .WithMaxIterationCount(1000).WithMinIterationCount(64)
#endif

                ;
        }
    }
}
