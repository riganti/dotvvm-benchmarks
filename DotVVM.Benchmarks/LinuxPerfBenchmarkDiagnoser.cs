using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using DotVVM.Framework.Utils;

namespace DotVVM.Benchmarks
{
    public class LinuxPerfBenchmarkDiagnoser : IDiagnoser
    {
        private readonly string tempPath;
        private ConcurrentQueue<Action> actionQueue = new ConcurrentQueue<Action>();
        private PerfHandler.CollectionHandler commandProcessor;

        public LinuxPerfBenchmarkDiagnoser(string tempPath = null, (string, string displayName)[] methodColumns = null, int maxParallelism = -1, bool enableRawPerfExport = false, bool enableStacksExport = false, bool allowDotnetMapgen = true)
        {
            this.tempPath = tempPath ?? Path.GetTempPath();
            this.methodColumns = methodColumns ?? new(string, string displayName)[0];
            this.allowDotnetMapgen = allowDotnetMapgen;
            this.maxParallelism = maxParallelism < 0 ? Environment.ProcessorCount : maxParallelism;
            this.rawExportFile = enableRawPerfExport ? new Dictionary<Benchmark, string>() : null;
            this.stacksExportFile = enableStacksExport ? new Dictionary<Benchmark, string>() : null;
        }

        private readonly Dictionary<Benchmark, string> rawExportFile;
        private readonly Dictionary<Benchmark, string> stacksExportFile;
        private ConcurrentDictionary<Benchmark, float[]> methodPercentiles = new ConcurrentDictionary<Benchmark, float[]>();

        private (string key, string displayName)[] methodColumns;
        private readonly bool allowDotnetMapgen;
        private int maxParallelism;
        private Benchmark currentBenchmark;

        public IEnumerable<string> Ids => new [] { nameof(LinuxPerfBenchmarkDiagnoser) };

        public IEnumerable<IExporter> Exporters => new IExporter[0];

        public void AfterSetup(DiagnoserActionParameters parameters)
        {
        }

        public void BeforeAnythingElse(DiagnoserActionParameters parameters)
        {
            Environment.SetEnvironmentVariable("COMPlus_PerfMapEnabled", "1");
        }

        private void ProcessTrace((string[] stack, int number)[] stacks, Benchmark benchmark)
        {
            Console.WriteLine($"Processing Trace ({stacks.Length} unique stacks)");
            var times = methodColumns.Select(t => (float)stacks.Where(s => s.stack.Any(m => m == t.key || m.Contains(t.key))).Select(s => s.number).Sum()).ToArray();
            var max = times.First();
            methodPercentiles.TryAdd(benchmark, times.Select(t => t / max).ToArray());
            Console.WriteLine($"{benchmark.DisplayInfo}: {string.Join(", ", methodPercentiles[benchmark])}");
        }

        public void BeforeGlobalCleanup(DiagnoserActionParameters parameters)
        {
            var ll = commandProcessor.StopAndLazyMerge();
            var benchmark = parameters.Benchmark;
            Debug.Assert(benchmark == currentBenchmark);
            actionQueue.Enqueue(() => {
                var stacks = ll();
                ProcessTrace(stacks, benchmark);
            });
            if (actionQueue.Count > 2) FlushQueue();
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
            new CompositeLogger(parameters.Config.GetLoggers().ToArray()).WriteLineInfo("Starting sampling profiler.");
            if (commandProcessor != null) throw new Exception("Collection is already running.");

            var folderInfo = parameters.Benchmark.Parameters?.FolderInfo;
            if (string.IsNullOrEmpty(folderInfo)) folderInfo = parameters.Benchmark.FolderInfo;
            folderInfo = new string(folderInfo.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            string path = Path.Combine(tempPath, "benchmarkLogs", (parameters.Benchmark.Parameters?.FolderInfo ?? parameters.Benchmark.FolderInfo).Replace("/", "_") + "_" + Guid.NewGuid().ToString().Replace("-", "_") + ".perfdata");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            rawExportFile?.Add(parameters.Benchmark, path);
            stacksExportFile?.Add(parameters.Benchmark, Path.ChangeExtension(path, "stacks.gz"));
            try
            {
                if (this.allowDotnetMapgen)
                    PerfHandler.ExecMapgen(parameters.Process);
            }
            catch { }
            try
            {
                commandProcessor = PerfHandler.StartCollection(path, parameters.Process, this.rawExportFile == null, stacksExportFile?.GetValue(parameters.Benchmark), this.allowDotnetMapgen);
                currentBenchmark = parameters.Benchmark;
            }
            catch (Exception ex)
            {
                rawExportFile?.Remove(parameters.Benchmark);
                new CompositeLogger(parameters.Config.GetLoggers().ToArray()).WriteLineError("Could not start trace: " + ex);
            }
        }

        public void DisplayResults(ILogger logger)
        {
        }

        public IColumnProvider GetColumnProvider()
        {
            FlushQueue();
            return new SimpleColumnProvider(
                methodColumns.Select((m, i) => (IColumn)new MethodTimeFractionColumn(m.displayName, methodPercentiles, i))
                  .Concat(new[] { rawExportFile != null ? new FileNameColumn("perf.data file", "perf_data_file", rawExportFile) : null })
                  .Concat(new[] { stacksExportFile != null ? new FileNameColumn("CPU stacks file", "CPU_stacks_file", stacksExportFile) : null })
                  .Where(c => c != null)
                  .ToArray()
            );
        }

        public void ProcessResults(Benchmark benchmark, BenchmarkReport report)
        {
        }

        public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
        {
            yield break;
        }

        public RunMode GetRunMode(Benchmark benchmark) => RunMode.ExtraRun;

        public void AfterGlobalSetup(DiagnoserActionParameters parameters)
        {
        }
    }

    public class MethodTimeFractionColumn : IColumn
    {
        private readonly string displayName;
        private readonly IDictionary<Benchmark, float[]> dict;
        private readonly int mIndex;

        public MethodTimeFractionColumn(string displayName, IDictionary<Benchmark, float[]> dict, int mIndex)
        {
            this.displayName = displayName;
            this.dict = dict;
            this.mIndex = mIndex;
        }

        public string Id => nameof(MethodTimeFractionColumn) + "_" + displayName;

        public string ColumnName => $"{displayName}";

        public bool AlwaysShow => false;

        public ColumnCategory Category => ColumnCategory.Custom;

        public int PriorityInCategory => mIndex + 1200;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => "% of inclusive CPU stacks spent in the method";

        public string GetValue(Summary summary, Benchmark benchmark) =>
            GetValue(summary, benchmark, new SummaryStyle { PrintUnitsInContent = true });

        public string GetValue(Summary summary, Benchmark benchmark, ISummaryStyle style) =>
            dict.TryGetValue(benchmark, out var times) ?
            $"{times[mIndex] * 100f}{( style.PrintUnitsInContent ? "%" : "")}" :
            "-";

        public bool IsAvailable(Summary summary) => true;

        public bool IsDefault(Summary summary, Benchmark benchmark) => !dict.ContainsKey(benchmark);
    }
    public class FileNameColumn : IColumn
    {
        private readonly Dictionary<Benchmark, string> fileName;

        public FileNameColumn(string colName, string id, Dictionary<Benchmark, string> fileName)
        {
            Id = id;
            ColumnName = colName;
            this.fileName = fileName;
        }

        public string Id { get; }

        public string ColumnName { get; }

        public bool AlwaysShow => false;

        public ColumnCategory Category => ColumnCategory.Custom;

        public int PriorityInCategory => 0;
        public bool IsNumeric => false;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => "File Name";

        public string GetValue(Summary summary, Benchmark benchmark)
        {
            if (fileName.TryGetValue(benchmark, out var val) && File.Exists(val))
            {
                return val;
            }
            return "-";
        }

        public string GetValue(Summary summary, Benchmark benchmark, ISummaryStyle style)
        {
            return GetValue(summary, benchmark);
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
