using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using BenchmarkDotNet.Analysers;

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

        public void BeforeGlobalCleanup(DiagnoserActionParameters parameters)
        {
            try
            {
                var totalTime = DateTime.Now - startTime;
                var kernelTime = parameters.Process.PrivilegedProcessorTime - startKernelCpuTime;
                var userTime = parameters.Process.UserProcessorTime - startUserCpuTime;
                var cpuTime = parameters.Process.TotalProcessorTime - startCpuTime;

                this.TotalTimeColum.AddValue(parameters.BenchmarkCase, ((cpuTime / totalTime) * 100).ToString(), "%");
                this.KernelTimeColum.AddValue(parameters.BenchmarkCase, ((kernelTime / totalTime) * 100).ToString(), "%");
                this.UserTimeColum.AddValue(parameters.BenchmarkCase, ((userTime / totalTime) * 100).ToString(), "%");
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

        public IColumnProvider GetColumnProvider()
        {
            return new SimpleColumnProvider(
                this.TotalTimeColum,
                this.KernelTimeColum,
                this.UserTimeColum
            );
        }

        public RunMode GetRunMode(BenchmarkCase benchmark) => RunMode.ExtraRun;

        public void ProcessResults(BenchmarkCase benchmark, BenchmarkReport report)
        {
        }

        public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
        {
            yield break;
        }

        public void Handle(HostSignal signal, DiagnoserActionParameters parameters)
        {
            if (signal == HostSignal.BeforeActualRun)
                BeforeMainRun(parameters);
            else if (signal == HostSignal.AfterAll)
                BeforeGlobalCleanup(parameters);
        }

        public void ProcessResults(DiagnoserResults results)
        {
        }

        public void DisplayResults(ILogger logger)
        {
        }

        public IEnumerable<IAnalyser> Analysers => Array.Empty<IAnalyser>();
    }

    public class GenericColumn : IColumn
    {
        readonly Dictionary<BenchmarkCase, Func<ISummaryStyle, string>> values = new Dictionary<BenchmarkCase, Func<ISummaryStyle, string>>();
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

        public void AddValue(BenchmarkCase benchmark, Func<ISummaryStyle, string> fn) => values.Add(benchmark, fn);

        public void AddValue(BenchmarkCase benchmark, string val, string unit) => AddValue(benchmark, s => s.PrintUnitsInContent ? val + unit : val);

        public string GetValue(Summary summary, BenchmarkCase benchmark) => GetValue(summary, benchmark, new SummaryStyle { PrintUnitsInContent = true, PrintUnitsInHeader = true });

        public string GetValue(Summary summary, BenchmarkCase benchmark, ISummaryStyle style) =>
            values.TryGetValue(benchmark, out var fn) ? fn(style) :
            "-";

        public bool IsAvailable(Summary summary) => values.Count > 0;

        public bool IsDefault(Summary summary, BenchmarkCase benchmark) => !values.ContainsKey(benchmark);
    }
}
