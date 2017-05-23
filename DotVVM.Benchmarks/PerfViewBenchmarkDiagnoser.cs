using System;
using System.Collections.Concurrent;
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
using DotVVM.Framework.Utils;

namespace DotVVM.Benchmarks
{
    public class PerfViewBenchmarkDiagnoser : IDiagnoser
    {
        private readonly string tempPath;
        private ConcurrentQueue<Action> actionQueue = new ConcurrentQueue<Action>();

        public PerfViewBenchmarkDiagnoser(string tempPath = null, (string, string displayName)[] methodColumns = null, int maxParallelism = -1)
        {
            this.tempPath = tempPath ?? Path.GetTempPath();
            this.methodColumns = methodColumns ?? new(string, string displayName)[0];
            this.maxParallelism = maxParallelism < 0 ? Environment.ProcessorCount : maxParallelism;
        }

        private Dictionary<Benchmark, string> logFile = new Dictionary<Benchmark, string>();
        private ConcurrentDictionary<Benchmark, float[]> methodPercentiles = new ConcurrentDictionary<Benchmark, float[]>();

        private PerfViewHandler.CollectionHandler commandProcessor = null;
        private (string key, string displayName)[] methodColumns;
        private int maxParallelism;
        private Benchmark currentBenchmark;

        //private CommandLineArgs commandArgs = null;

        public void AfterSetup(DiagnoserActionParameters parameters)
        {

        }

        public void BeforeAnythingElse(DiagnoserActionParameters parameters)
        {
        }

        private void ProcessTrace(Dictionary<string, ETWHelper.CallTreeItem> callTree, Benchmark benchmark)
        {
            var times = ETWHelper.ComputeTimeFractions(callTree, methodColumns.Select(t => t.key).ToArray()).ToArray();
            var serializedTree = callTree.OrderByDescending(k => k.Value.IncSamples).Select(t => $"\"{t.Key}\",{t.Value.IncSamples},{t.Value.Samples}");
            File.WriteAllLines(logFile[benchmark] + ".methods.csv", serializedTree);
            methodPercentiles.TryAdd(benchmark, times);
        }

        public void BeforeCleanup()
        {
            var ll = commandProcessor.StopAndLazyMerge();
            var benchmark = currentBenchmark;
            if (actionQueue.Count > 8) FlushQueue();
            actionQueue.Enqueue(() => {
                var stacks = ll();
                ProcessTrace(stacks, benchmark);
            });
            commandProcessor = null;
        }

        void FlushQueue()
        {
            var list = new List<Action>();
            while (actionQueue.TryDequeue(out var a)) list.Add(a);
            Parallel.ForEach(list, a => a());
        }

        public void BeforeMainRun(DiagnoserActionParameters parameters)
        {
            if (commandProcessor != null) throw new Exception("Collection is already running.");

            var folderInfo = parameters.Benchmark.Parameters?.FolderInfo;
            if (string.IsNullOrEmpty(folderInfo)) folderInfo = parameters.Benchmark.FolderInfo;
            folderInfo = new string(folderInfo.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            string path = Path.Combine(tempPath, "benchmarkLogs", (parameters.Benchmark.Parameters?.FolderInfo ?? parameters.Benchmark.FolderInfo).Replace("/", "_") + "_" + Guid.NewGuid().ToString().Replace("-", "_") + ".etl.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            logFile.Add(parameters.Benchmark, path);
            try
            {
                commandProcessor = PerfViewHandler.StartCollection(path, parameters.Process);
                currentBenchmark = parameters.Benchmark;
            }
            catch(Exception ex)
            {
                logFile.Remove(parameters.Benchmark);
                new CompositeLogger(parameters.Config.GetLoggers().ToArray()).WriteLineError("Could not start ETW trace: " + ex);
            }
        }

        public void DisplayResults(ILogger logger)
        {
        }

        public IColumnProvider GetColumnProvider()
        {
            FlushQueue();
            return new SimpleColumnProvider(
                methodColumns.Select((m, i) => (IColumn)new MethodPercentileColumn(m.displayName, methodPercentiles, i))
                .Concat(new[] { new FileNameColumn(logFile) }).ToArray()
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

    public class MethodPercentileColumn : IColumn
    {
        private readonly string displayName;
        private readonly IDictionary<Benchmark, float[]> dict;
        private readonly int mIndex;

        public MethodPercentileColumn(string displayName, IDictionary<Benchmark, float[]> dict, int mIndex)
        {
            this.displayName = displayName;
            this.dict = dict;
            this.mIndex = mIndex;
        }
        
        public string Id => nameof(MethodPercentileColumn) + "_" + displayName;

        public string ColumnName => $"{displayName}";

        public bool AlwaysShow => false;

        public ColumnCategory Category => ColumnCategory.Custom;

        public int PriorityInCategory => mIndex + 1200;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => "Method Percentile";

        public string GetValue(Summary summary, Benchmark benchmark) =>
            dict.TryGetValue(benchmark, out var times) ?
            $"{times[mIndex] * 100f}%" :
            "-";

        public string GetValue(Summary summary, Benchmark benchmark, ISummaryStyle style)
        {
            return GetValue(summary, benchmark);
        }

        public bool IsAvailable(Summary summary) => true;

        public bool IsDefault(Summary summary, Benchmark benchmark) => false;
    }
}
