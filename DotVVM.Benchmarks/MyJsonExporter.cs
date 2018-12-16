using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Diagnosers;
using System.Collections.Concurrent;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Parameters;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Environments;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Horology;

// #if C_77b3b6f || DEBUG
// #else
// #error You should not build the solution by yourself (except for debugging purposes), use BenchmarkRunner
// #endif


namespace DotVVM.Benchmarks
{
    public class MyJsonExporter: ExporterBase
    {
        protected override string FileExtension => "json";

        private bool IndentJson { get; set; }

        public MyJsonExporter(bool indentJson = true)
        {
            IndentJson = indentJson;
        }

        public override void ExportToLog(Summary summary, ILogger logger)
        {
            // We construct HostEnvironmentInfo manually, so that we can have the HardwareTimerKind enum as text, rather than an integer
            // SimpleJson serialiser doesn't seem to have an enum String/Value option (to-be-fair, it is meant to be "Simple")
            var environmentInfo = new
            {
                HostEnvironmentInfo.BenchmarkDotNetCaption,
                summary.HostEnvironmentInfo.BenchmarkDotNetVersion,
                OsVersion = summary.HostEnvironmentInfo.OsVersion.Value,
                summary.HostEnvironmentInfo.CpuInfo,
                summary.HostEnvironmentInfo.RuntimeVersion,
                summary.HostEnvironmentInfo.Architecture,
                summary.HostEnvironmentInfo.HasAttachedDebugger,
                summary.HostEnvironmentInfo.HasRyuJit,
                summary.HostEnvironmentInfo.Configuration,
                summary.HostEnvironmentInfo.JitModules,
                DotNetCliVersion = summary.HostEnvironmentInfo.DotNetSdkVersion.Value,
                summary.HostEnvironmentInfo.ChronometerFrequency,
                HardwareTimerKind = summary.HostEnvironmentInfo.HardwareTimerKind.ToString()
            };

            // If we just ask SimpleJson to serialise the entire "summary" object it throws several errors.
            // So we are more specific in what we serialise (plus some fields/properties aren't relevant)

            var columns = summary.GetColumns()
                // exclude
                .Where(col => !(col is BenchmarkDotNet.Columns.StatisticColumn || col is BenchmarkDotNet.Columns.TargetMethodColumn || col is BenchmarkDotNet.Columns.ParamColumn))
                .ToArray();
            var summaryStyle = new SummaryStyle { PrintUnitsInContent = false, PrintUnitsInHeader = true, SizeUnit = SizeUnit.B, TimeUnit = TimeUnit.Nanosecond };

            var benchmarks = summary.Reports.Select(r =>
            {
                var data = new Dictionary<string, object> {
                    // We don't need Benchmark.ShortInfo, that info is available via Benchmark.Parameters below
                    { "DisplayInfo", r.BenchmarkCase.DisplayInfo },
                    { "Namespace", r.BenchmarkCase.Descriptor.Type.Namespace },
                    { "Type", r.BenchmarkCase.Descriptor.Type.Name },
                    { "Method", r.BenchmarkCase.Descriptor.WorkloadMethod.Name },
                    { "MethodTitle", r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo },
                    { "Parameters", r.BenchmarkCase.Parameters.Items.ToDictionary(p => p.Name, p => p.Value) },
                    // { "Properties", r.Benchmark.Job.ToSet().ToDictionary(p => p.Name, p => p.Value) }, // TODO
                    { "Statistics", r.ResultStatistics },
                    { "Columns", columns.Where(col => !col.IsDefault(summary, r.BenchmarkCase)).ToDictionary(col => col.Id, col => col.GetValue(summary, r.BenchmarkCase, summaryStyle)) }
                };

                // We show MemoryDiagnoser's results only if it is being used
                if(summary.Config.GetDiagnosers().OfType<SynchronousMemoryDiagnoser>().FirstOrDefault() is SynchronousMemoryDiagnoser smd &&
                    smd.FindGCStats(r.BenchmarkCase) is GcStats gcStats)
                {
                    data.Add("Memory", gcStats);
                }
                else if(summary.Config.GetDiagnosers().Any(diagnoser => diagnoser is MemoryDiagnoser))
                {
                    data.Add("Memory", r.GcStats);
                }

                return data;
            });

            var resultObj = new Dictionary<string, object> {
                { "Title", summary.Title },
                { "HostEnvironmentInfo", environmentInfo },
                { "Columns", columns.ToDictionary(col => col.Id, col => new {
                    col.ColumnName,
                    col.Category,
                    col.IsNumeric,
                    col.Legend,
                    col.PriorityInCategory,
                    col.UnitType,
                    IsFileName = col is FileNameColumn
                }) },
                { "Benchmarks", benchmarks },
            };

            logger.WriteLine(JsonConvert.SerializeObject(resultObj, Formatting.Indented, new JsonConverter[] { new StringEnumConverter() }));
        }
    }
}