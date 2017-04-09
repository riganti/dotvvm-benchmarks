using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Diagnostics.Windows;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

namespace DotVVM.Benchmarks
{
    //public class EtwLogDiagnoser: EtwDiagnoser<EtwLogDiagnoser.Stats>, IDiagnoser
    //{
    //    public EtwLogDiagnoser(string tempPath = null)
    //    {
    //        this.tempPath = tempPath ?? Path.GetTempPath();
    //    }

    //    protected override ulong EventType => 0;//unchecked((ulong)(KernelTraceEventParser.Keywords.PMCProfile | KernelTraceEventParser.Keywords.Profile));

    //    protected override string SessionNamePrefix => "EtwProfiler";
    //    private Dictionary<Benchmark, string> logFile = new Dictionary<Benchmark, string>();
    //    private readonly string tempPath;

    //    protected override TraceEventSession CreateSession(Benchmark benchmark)
    //    {
    //        var name = $"{this.SessionNamePrefix}-{benchmark.FolderInfo}-{benchmark.Parameters?.FolderInfo ?? "ll"}";
    //        string path = Path.Combine(tempPath, "benchmarkLogs", (benchmark.Parameters?.FolderInfo ?? benchmark.FolderInfo) + "_" + Guid.NewGuid().ToString().Replace("-", "_") + ".etl");
    //        Directory.CreateDirectory(Path.GetDirectoryName(path));
    //        logFile.Add(benchmark, path);
    //        return new TraceEventSession(name) { };
    //    }

    //    protected override void AttachToEvents(TraceEventSession traceEventSession, Benchmark benchmark)
    //    {
    //        //traceEventSession.Source.Kernel.PerfInfoCollectionStart += _ => {  };
    //    }

    //    public IColumnProvider GetColumnProvider()
    //    {
    //        return new SimpleColumnProvider(
    //            new FileNameColumn(logFile)
    //        );
    //    }

    //    public void BeforeAnythingElse(DiagnoserActionParameters parameters) { }

    //    public void AfterSetup(DiagnoserActionParameters parameters) { }

    //    public void BeforeMainRun(DiagnoserActionParameters parameters)
    //    {
    //        Start(parameters);
    //    }

    //    public void BeforeCleanup()
    //    {
    //        Stop();
    //    }

    //    public void ProcessResults(Benchmark benchmark, BenchmarkReport report) { }

    //    public void DisplayResults(ILogger logger) { }

    //    public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
    //    {
    //        yield break;
    //    }

    //    public class Stats { }


    //}

    public class FileNameColumn : IColumn
    {
        private readonly Dictionary<Benchmark, string> fileName;

        public FileNameColumn(Dictionary<Benchmark, string> fileName)
        {
            this.fileName = fileName;
        }

        public string Id => typeof(FileNameColumn).FullName;

        public string ColumnName => "ETW log file";

        public bool AlwaysShow => false;

        public ColumnCategory Category => ColumnCategory.Custom;

        public int PriorityInCategory => 0;

        public string GetValue(Summary summary, Benchmark benchmark)
        {
            if (fileName.TryGetValue(benchmark, out var val))
            {
                return val;
                //var outFile = Path.Combine("etwTraces", Path.GetFileName(val));
                //Directory.CreateDirectory(Path.GetDirectoryName(outFile));
                //File.Copy(val, Path.Combine(summary.ResultsDirectoryPath, outFile));
                //File.Delete(val);
                //return outFile;
            }
            return "-";
        }

        public bool IsAvailable(Summary summary)
        {
            return true;
        }

        public bool IsDefault(Summary summary, Benchmark benchmark)
        {
            return false;
        }
    }
}
