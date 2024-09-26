#define RUN_perf_samples
// #define RUN_dotvvm_samples
// #define RUN_manytargets
#define RUN_synth_tests
#define PRECISE_RUN
// #define DEBUG_RUN
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
using BenchmarkDotNet.Toolchains.Parameters;
using BenchmarkDotNet.Exporters.Csv;
using System.Globalization;
using BenchmarkDotNet.Columns;
using Microsoft.AspNetCore.Routing.Tree;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using BenchmarkDotNet.Environments;
using Microsoft.Diagnostics.Runtime;
using BenchmarkDotNet.Toolchains.CsProj;

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
            return app.UseDotVVM<DotVVM.Samples.BasicSamples.DotvvmStartup>(Path.Combine(currentPath, "dotvvm/src/Samples/Common"));
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

            // Environment.SetEnvironmentVariable("COMPlus_PerfMapEnabled", "1");
            if (Directory.Exists("testViewModels")) Directory.Delete("testViewModels", true);

            var conf = CreateTestConfiguration();

            var b = new List<BenchmarkRunInfo>();
            // b.AddRange(DotvvmSamplesBenchmarker<DotvvmPerfTestsLauncher>.BenchmarkSamples(conf, getRequests: true, postRequests: false));
#if RUN_synth_tests
            b.Add(BenchmarkConverter.TypeToBenchmarks(typeof(Benchmarks.HtmlWriterBenchmarks), conf));
            // b.Add(BenchmarkConverter.TypeToBenchmarks(typeof(Benchmarks.RequestBenchmarks), conf));
            // b.Add(BenchmarkConverter.TypeToBenchmarks(typeof(Benchmarks.ParserBenchmarks), conf));
            // b.Add(BenchmarkConverter.TypeToBenchmarks(typeof(Benchmarks.SingleControlTests), conf));
#endif
#if RUN_dotvvm_samples
            b.AddRange(
                DotvvmSamplesBenchmarker<DotvvmSamplesLauncher>.BenchmarkSamples(conf, postRequests: true, getRequests: true)
#if DEBUG_RUN
                    .Select(p => new BenchmarkRunInfo(
                        p.BenchmarksCases.Where(bcase => bcase.Parameters.Items.Any(p => {
                            Console.WriteLine(p.Name + " " + p.Value);
                            return p.Name == "Url" && (p.Value + "") == "/ComplexSamples/TaskList/ServerRenderedTaskList";
                        })).ToArray(),
                        p.Type,
                        p.Config
                    ))
#else
                    .Select(p => new BenchmarkRunInfo(
                        p.BenchmarksCases.Where(bcase => bcase.Parameters.Items.Any(p => {
                            return p.Name == "Url" && (p.Value + "") is "/ComplexSamples/TaskList/ServerRenderedTaskList" or "/ControlSamples/GridView/LargeGrid" or "/ControlSamples/GridView/GridViewPagingSorting" or "/FeatureSamples/AutoUI/AutoForm" or "/FeatureSamples/AutoUI/AutoGridViewColumns" or "/FeatureSamples/PostBack/ConfirmPostBackHandler" or "ControlSamples/IncludeInPageProperty/IncludeInPage" or "/FeatureSamples/FormControlsEnabled/FormControlsEnabled" or "/ControlSamples/TextBox/TextBox_Format" or "/ControlSamples/TextBox/TextBox_Format_Binding";
                        })).ToArray(),
                        p.Type,
                        p.Config
                    ))

#endif
            );
#endif
#if RUN_perf_samples
            b.AddRange(DotvvmSamplesBenchmarker<DotvvmPerfTestsLauncher>.BenchmarkSamples(conf, getRequests: true, postRequests: true));
            Console.WriteLine("Running with perf samples");
#endif
#if RUN_aspnet_mvc
            b.AddRange(DotvvmSamplesBenchmarker<MvcWebApp.MvcAppLauncher>.BenchmarkMvcSamples(conf));
#endif

            BenchmarkRunner.Run(
                //b.GroupBy(t => t.Parameters.Items.Any(p => p.Name == nameof(DotvvmPostbackBenchmarks<DotvvmSamplesLauncher>.SerializedViewModel))).SelectMany(g => g.Take(27).Skip(25))
                b
                //b.Take(1)
                .ToArray());
        }

        static IConfig CreateTestConfiguration()
        {
            var machineCulture = new CultureInfo("");
            machineCulture.NumberFormat.NumberGroupSeparator = "";
            machineCulture.NumberFormat.PercentGroupSeparator = "";
            machineCulture.NumberFormat.CurrencyGroupSeparator = "";
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

#if RUN_manytargets
            var jobs =
                from runtime in new[] {
                    (runtime: CoreRuntime.Core60, toolchain: CsProjCoreToolchain.NetCoreApp60),
                    (runtime: CoreRuntime.Core70, toolchain: CsProjCoreToolchain.NetCoreApp70),
                    (runtime: CoreRuntime.Core80, toolchain: CsProjCoreToolchain.NetCoreApp80),
                }
                from gcServer in new[] {
                    true,
                    false
                }
                from gcConcurrent in new[] {
                    true,
                    // false
                }
                from pgoEnvVars in (new[] {
                    new [] { new EnvironmentVariable("DOTNET_TC_QuickJitForLoops", "1"), new EnvironmentVariable("DOTNET_ReadyToRun", "0"), new EnvironmentVariable("DOTNET_TieredPGO", "1") },
                    new [] { new EnvironmentVariable("DOTNET_TieredPGO", "0") }
                })
                from bullshitEnvVars in (new[] {
                    new [] { new EnvironmentVariable("BS", "1") },
                    Array.Empty<EnvironmentVariable>()
                })
                from affinity in new[] {
                    new IntPtr(1),
                    new IntPtr(63),
                    new IntPtr(63 << 24)
                }

                where pgoEnvVars.Length == 1 || runtime.runtime == CoreRuntime.Core70 || runtime.runtime == CoreRuntime.Core80

                select WithRunCount(
                    new Job().WithJit(Jit.RyuJit).WithGcServer(gcServer).WithGcForce(false).WithAffinity(affinity).WithGcConcurrent(gcConcurrent).WithEnvironmentVariables(pgoEnvVars.Concat(bullshitEnvVars).ToArray()),
                    runtime.runtime,
                    runtime.toolchain
                );
            foreach (var job in jobs)
            {
                Console.WriteLine(job);
                conf.AddJob(job);
            }
#else
            var cpu1 = new IntPtr(63);
            var cpu2 = new IntPtr(63 << 8);
            conf.AddJob(WithRunCount(Job.RyuJitX64.WithGcForce(false).WithAffinity(cpu1), CoreRuntime.Core60, BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.NetCoreApp60));
            conf.AddJob(WithRunCount(Job.RyuJitX64.WithGcForce(false).WithAffinity(cpu1), CoreRuntime.Core70, BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.NetCoreApp70));
            conf.AddJob(WithRunCount(Job.RyuJitX64.WithGcForce(false).WithAffinity(cpu1), CoreRuntime.Core80, BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.NetCoreApp80).WithEnvironmentVariable("DOTNET_TieredPGO", "0"));
            conf.AddJob(WithRunCount(Job.RyuJitX64.WithGcForce(false).WithAffinity(cpu2), CoreRuntime.Core80, BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.NetCoreApp80));
            conf.AddJob(WithRunCount(Job.RyuJitX64.WithGcForce(false).WithAffinity(cpu1), CoreRuntime.Core80, BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.NetCoreApp80));
            // conf.AddJob(
            //     WithRunCount(Job.RyuJitX64.WithGcForce(false), CoreRuntime.Core70, BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.NetCoreApp70)
            //         .WithAffinity(new IntPtr(63 << 24))
            // );
            // conf.AddJob(WithRunCount(Job.RyuJitX64.WithGcForce(false), CoreRuntime.Core70, BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.NetCoreApp70).WithEnvironmentVariable("DOTNET_TC_QuickJitForLoops", "1").WithEnvironmentVariable("DOTNET_ReadyToRun", "0").WithEnvironmentVariable("DOTNET_TieredPGO", "1").WithEnvironmentVariables());
#endif
            conf.WithOptions(ConfigOptions.DisableOptimizationsValidator | ConfigOptions.JoinSummary);
            // conf.AddExporter(new MyJsonExporter(conf));
            // conf.Expo

            conf.WithSummaryStyle(conf.SummaryStyle.WithMaxParameterColumnWidth(99999).WithCultureInfo(CultureInfo.InvariantCulture));
            conf.AddExporter(new CsvExporter(CsvSeparator.Comma, new SummaryStyle(
                machineCulture,
                printUnitsInHeader: true,
                sizeUnit: SizeUnit.B,
                timeUnit: Perfolizer.Horology.TimeUnit.Microsecond,
                printUnitsInContent: false,
                printZeroValuesInContent: true,
                maxParameterColumnWidth: 99999
            )));

            conf.AddColumn(BenchmarkDotNet.Columns.StatisticColumn.Median);
            conf.AddColumn(BenchmarkDotNet.Columns.StatisticColumn.OperationsPerSecond);
            conf.AddColumn(BenchmarkDotNet.Columns.StatisticColumn.Min);
            conf.AddColumn(BenchmarkDotNet.Columns.StatisticColumn.P95);
            conf.AddColumn(BenchmarkDotNet.Columns.StatisticColumn.CiLower(Perfolizer.Mathematics.Common.ConfidenceLevel.L95));
            conf.AddColumn(BenchmarkDotNet.Columns.StatisticColumn.CiUpper(Perfolizer.Mathematics.Common.ConfidenceLevel.L95));

            conf.AddDiagnoser(new CpuTimeDiagnoser());
// #if DIAGNOSER_cpu_sampling
//             var benchmarkDiagnoser = new LinuxPerfBenchmarkDiagnoser(methodColumns: methodColumns, enableStacksExport: true,
// #if DEBUG || EXPORT_rawperf
//                 enableRawPerfExport: true
// #else
//                 enableRawPerfExport: false
// #endif
//             );
//             conf.Add(benchmarkDiagnoser);
//             benchmarkDiagnoser.AddColumnsToConfig(conf);
//             Console.WriteLine("CPU Sampling [ON]");
//             conf.AddDiagnoser(SynchronousMemoryDiagnoser.Default);
// #else
//             // conf.Add(MemoryDiagnoser.Default)
// #endif
            // conf.AddDiagnoser(SynchronousMemoryDiagnoser.Default);
            conf.AddDiagnoser(MemoryDiagnoser.Default);
            return conf;
        }

        private static Job WithRunCount(Job job, Runtime runtime = null, IToolchain toolchain = null)
        {
            job = new Job(job);
            runtime ??= CoreRuntime.Core60;
            toolchain ??= BenchmarkDotNet.Toolchains.CsProj.CsProjCoreToolchain.NetCoreApp60;
            job.Infrastructure.Toolchain = new Toolchain(toolchain.Name, toolchain.Generator, new SynchonousBuilder(toolchain.Builder), new InterceptingExecutor(toolchain.Executor));
            // job = job.WithMinIterationTime(BenchmarkDotNet.Horology.TimeInterval.FromMilliseconds(200));
            //job.Run.WarmupCount = 1;
            return job
                .WithRuntime(runtime)
                .WithArguments(new MsBuildArgument[] { new MsBuildArgument("--disable-parallel") })
#if DEBUG_RUN
                .WithMaxIterationCount(8).WithMinIterationCount(6).WithWarmupCount(1)
#elif PRECISE_RUN
                .WithMaxRelativeError(0.008)
                .WithMaxIterationCount(1000).WithMinIterationCount(64)
#else
                .WithMaxIterationCount(80).WithMinIterationCount(10).WithWarmupCount(2)
#endif
                // .WithMinIterationCount(128)

                ;
        }
    }
}
