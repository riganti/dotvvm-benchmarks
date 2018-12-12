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
        static readonly GenericMetricDescriptor TotalTimeColum = new GenericMetricDescriptor("CPUTime.TotalTime", "Total CPU Time", UnitType.Dimensionless, "% of total CPU time", "%", false);
        static readonly GenericMetricDescriptor KernelTimeColum = new GenericMetricDescriptor("CPUTime.KernelTime", "Kernel CPU Time", UnitType.Dimensionless, "% of CPU time spent in kernel", "%", false);
        static readonly GenericMetricDescriptor UserTimeColum = new GenericMetricDescriptor("CPUTime.UserTime", "User CPU Time", UnitType.Dimensionless, "% of CPU time spent in userspace", "%", false);

        readonly Dictionary<BenchmarkCase, (double, double, double)> results = new Dictionary<BenchmarkCase, (double, double, double)>();
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

                this.results.Add(parameters.BenchmarkCase, (
                    ((cpuTime / totalTime) * 100),
                    ((kernelTime / totalTime) * 100),
                    ((userTime / totalTime) * 100)
                ));
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

        public RunMode GetRunMode(BenchmarkCase benchmark) => RunMode.ExtraRun;
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

        public IEnumerable<Metric> ProcessResults(DiagnoserResults results)
        {
            var (total, kernel, user) = this.results[results.BenchmarkCase];
            yield return new Metric(TotalTimeColum, total);
            yield return new Metric(KernelTimeColum, kernel);
            yield return new Metric(UserTimeColum, user);
        }

        public void DisplayResults(ILogger logger)
        {
        }

        public IEnumerable<IAnalyser> Analysers => Array.Empty<IAnalyser>();
    }

    public class GenericMetricDescriptor : IMetricDescriptor
    {
        public GenericMetricDescriptor(string id, string displayName, UnitType unitType, string legend, string unit, bool theGreaterTheBetter, string numberFormat = "G")
        {
            this.Id = id;
            this.DisplayName = displayName;
            this.UnitType = unitType;
            this.Legend = legend;
            this.NumberFormat = numberFormat;
            this.Unit = unit;
            this.TheGreaterTheBetter = theGreaterTheBetter;
        }
        public string Id { get; }

        public string DisplayName { get; }

        public string Legend { get; }

        public string NumberFormat { get; }

        public UnitType UnitType { get; }

        public string Unit { get; }

        public bool TheGreaterTheBetter { get; }
    }
}
