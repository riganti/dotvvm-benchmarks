using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using PerfView;

namespace DotVVM.Benchmarks
{
    public class PerfViewBenchmarkDiagnoser : IDiagnoser
    {
        private readonly string tempPath;

        public PerfViewBenchmarkDiagnoser(string tempPath = null)
        {
            this.tempPath = tempPath ?? Path.GetTempPath();
        }

        private Dictionary<Benchmark, string> logFile = new Dictionary<Benchmark, string>();

        private PerfViewHandler.CollectionHandler commandProcessor = null;
        //private CommandLineArgs commandArgs = null;

        public void AfterSetup(DiagnoserActionParameters parameters)
        {

        }

        public void BeforeAnythingElse(DiagnoserActionParameters parameters)
        {
        }

        public void BeforeCleanup()
        {
            commandProcessor.Stop();
            commandProcessor = null;
        }

        protected CommandLineArgs CreateArgs(string outFile, string processName = null)
        {
            var a = new CommandLineArgs();
            a.ParseArgs(new[] { "/NoGui" });
            a.RestartingToElevelate = ""; // this should prevent PerfView from trying to elevate itself
            a.Zip = true;
            a.Merge = true;
            a.InMemoryCircularBuffer = false;
            //a.DotNetAllocSampled = true;
            a.CpuSampleMSec = 2;//0.125f;
            a.DataFile = outFile;
            a.Process = processName ?? a.Process;
            a.NoNGenRundown = true;
            a.TrustPdbs = true;
            a.UnsafePDBMatch = true;
            return a;
        }

        public void BeforeMainRun(DiagnoserActionParameters parameters)
        {
            if (commandProcessor != null) throw new Exception("Collection is already running.");

            string path = Path.Combine(tempPath, "benchmarkLogs", (parameters.Benchmark.Parameters?.FolderInfo ?? parameters.Benchmark.FolderInfo) + "_" + Guid.NewGuid().ToString().Replace("-", "_") + ".etl.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            logFile.Add(parameters.Benchmark, path);

            commandProcessor = PerfViewHandler.StartCollection(path, parameters.Process.ProcessName);
        }

        public void DisplayResults(ILogger logger)
        {
        }

        public IColumnProvider GetColumnProvider()
        {
            return new SimpleColumnProvider(
                new FileNameColumn(logFile)
            );
        }

        public void ProcessResults(Benchmark benchmark, BenchmarkReport report)
        {
        }

        public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
        {
            yield break;
        }
    }
}
