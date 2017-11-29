using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

namespace DotVVM.Benchmarks
{
    public class CpuTimeDiagnoser : IDiagnoser
    {
        readonly GenericColumn TotalTimeColum = new GenericColumn("CPUTime.TotalTime", "Total CPU Time", true, UnitType.Dimensionless, "% of total CPU time");
        readonly GenericColumn KernelTimeColum = new GenericColumn("CPUTime.KernelTime", "Kernel CPU Time", true, UnitType.Dimensionless, "% of CPU time spent in kernel");
        readonly GenericColumn UserTimeColum = new GenericColumn("CPUTime.UserTime", "User CPU Time", true, UnitType.Dimensionless, "% of CPU time spent in userspace");
        public CpuTimeDiagnoser()
        {
        }

        public IEnumerable<string> Ids => new [] { nameof(CpuTimeDiagnoser) };

        public IEnumerable<IExporter> Exporters => Array.Empty<IExporter>();

        public void AfterGlobalSetup(DiagnoserActionParameters parameters)
        {
        }

        public void BeforeAnythingElse(DiagnoserActionParameters parameters)
        {
        }

        public void BeforeGlobalCleanup(DiagnoserActionParameters parameters)
        {
            try
            {
                var totalTime = DateTime.Now - startTime;
                var kernelTime = parameters.Process.PrivilegedProcessorTime - startKernelCpuTime;
                var userTime = parameters.Process.UserProcessorTime - startUserCpuTime;
                var cpuTime = parameters.Process.TotalProcessorTime - startCpuTime;
                Debugger.Break();

                this.TotalTimeColum.AddValue(parameters.Benchmark, ((cpuTime / totalTime) * 100).ToString(), "%");
                this.KernelTimeColum.AddValue(parameters.Benchmark, ((kernelTime / totalTime) * 100).ToString(), "%");
                this.UserTimeColum.AddValue(parameters.Benchmark, ((userTime / totalTime) * 100).ToString(), "%");
            }
            catch(Exception ex)
            {
                new CompositeLogger(parameters.Config.GetLoggers().ToArray()).WriteLineError(ex.ToString());
            }
            finally
            {
                startTime = default(DateTime);
                startCpuTime = startUserCpuTime = startKernelCpuTime = default(TimeSpan);
            }
        }

        DateTime startTime;
        TimeSpan startCpuTime;
        TimeSpan startUserCpuTime;
        TimeSpan startKernelCpuTime;

        public void BeforeMainRun(DiagnoserActionParameters parameters)
        {
            startTime = DateTime.Now;
            startCpuTime = parameters.Process.TotalProcessorTime;
            startUserCpuTime = parameters.Process.UserProcessorTime;
            startKernelCpuTime = parameters.Process.PrivilegedProcessorTime;
        }

        public void DisplayResults(ILogger logger)
        {
        }

        public IColumnProvider GetColumnProvider()
        {
            return new SimpleColumnProvider(
                this.TotalTimeColum,
                this.KernelTimeColum,
                this.UserTimeColum
            );
        }

        public RunMode GetRunMode(Benchmark benchmark) => RunMode.ExtraRun;

        public void ProcessResults(Benchmark benchmark, BenchmarkReport report)
        {
        }

        public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
        {
            yield break;
        }
    }

    public class GenericColumn : IColumn
    {
        readonly Dictionary<Benchmark, Func<ISummaryStyle, string>> values = new Dictionary<Benchmark, Func<ISummaryStyle, string>>();
        public GenericColumn(string id, string columnName, bool isNumeric, UnitType unitType, string legend)
        {
            this.Id = id;
            this.ColumnName = columnName;
            this.IsNumeric = isNumeric;
            this.UnitType = unitType;
            this.Legend = legend;
        }
        public string Id { get; }

        public string ColumnName { get; }

        public bool AlwaysShow => false;

        public ColumnCategory Category => ColumnCategory.Diagnoser;

        public int PriorityInCategory => 0;

        public bool IsNumeric { get; }

        public UnitType UnitType { get; }

        public string Legend { get; }

        public void AddValue(Benchmark benchmark, Func<ISummaryStyle, string> fn) => values.Add(benchmark, fn);

        public void AddValue(Benchmark benchmark, string val, string unit) => AddValue(benchmark, s => s.PrintUnitsInContent ? val + unit : val);

        public string GetValue(Summary summary, Benchmark benchmark) => GetValue(summary, benchmark, new SummaryStyle { PrintUnitsInContent = true, PrintUnitsInHeader = true });

        public string GetValue(Summary summary, Benchmark benchmark, ISummaryStyle style) =>
            values.TryGetValue(benchmark, out var fn) ? fn(style) :
            "-";

        public bool IsAvailable(Summary summary) => values.Count > 0;

        public bool IsDefault(Summary summary, Benchmark benchmark) => values.ContainsKey(benchmark);
    }
}