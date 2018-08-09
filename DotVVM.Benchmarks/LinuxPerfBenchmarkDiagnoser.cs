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
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using DotVVM.Framework.Utils;
using BenchmarkDotNet.Analysers;

namespace DotVVM.Benchmarks
{
    public class LinuxPerfBenchmarkDiagnoser : IDiagnoser
    {
        private readonly string tempPath;
        private ConcurrentQueue<Action> actionQueue = new ConcurrentQueue<Action>();
        private PerfHandler.CollectionHandler commandProcessor;

        public LinuxPerfBenchmarkDiagnoser(string tempPath = null, (string, string displayName)[] methodColumns = null, int maxParallelism = -1, bool enableRawPerfExport = false, bool enableStacksExport = false, bool allowDotnetMapgen = false)
        {
            this.tempPath = tempPath ?? Path.GetTempPath();
            this.methodColumns = methodColumns ?? new(string, string displayName)[0];
            this.allowDotnetMapgen = allowDotnetMapgen;
            this.maxParallelism = maxParallelism < 0 ? Environment.ProcessorCount : maxParallelism;
            this.rawExportFile = enableRawPerfExport ? new Dictionary<BenchmarkCase, string>() : null;
            this.stacksExportFile = enableStacksExport ? new Dictionary<BenchmarkCase, string>() : null;
        }

        private readonly Dictionary<BenchmarkCase, string> rawExportFile;
        private readonly Dictionary<BenchmarkCase, string> stacksExportFile;
        private ConcurrentDictionary<BenchmarkCase, float[]> methodPercentiles = new ConcurrentDictionary<BenchmarkCase, float[]>();

        private (string key, string displayName)[] methodColumns;
        private readonly bool allowDotnetMapgen;
        private int maxParallelism;
        private BenchmarkCase currentBenchmark;

        public IEnumerable<string> Ids => new [] { nameof(LinuxPerfBenchmarkDiagnoser) };

        public IEnumerable<IExporter> Exporters => new IExporter[0];

        public void BeforeAnythingElse(DiagnoserActionParameters parameters)
        {
            Environment.SetEnvironmentVariable("COMPlus_PerfMapEnabled", "1");
        }

        private void ProcessTrace((string[] stack, int number)[] stacks, BenchmarkCase benchmark)
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
            var benchmark = parameters.BenchmarkCase;
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

            var folderInfo = parameters.BenchmarkCase.Parameters?.FolderInfo;
            if (string.IsNullOrEmpty(folderInfo)) folderInfo = parameters.BenchmarkCase.FolderInfo;
            folderInfo = new string(folderInfo.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            string path = Path.Combine(tempPath, "benchmarkLogs", (parameters.BenchmarkCase.Parameters?.FolderInfo ?? parameters.BenchmarkCase.FolderInfo).Replace("/", "_") + "_" + Guid.NewGuid().ToString().Replace("-", "_") + ".perfdata");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            rawExportFile?.Add(parameters.BenchmarkCase, path);
            stacksExportFile?.Add(parameters.BenchmarkCase, Path.ChangeExtension(path, "stacks.gz"));
            try
            {
                if (this.allowDotnetMapgen)
                    PerfHandler.ExecMapgen(parameters.Process);
            }
            catch { }
            try
            {
                commandProcessor = PerfHandler.StartCollection(path, parameters.Process, this.rawExportFile == null, stacksExportFile?.GetValue(parameters.BenchmarkCase), this.allowDotnetMapgen);
                currentBenchmark = parameters.BenchmarkCase;
            }
            catch (Exception ex)
            {
                rawExportFile?.Remove(parameters.BenchmarkCase);
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


        public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
        {
            yield break;
        }

        public RunMode GetRunMode(BenchmarkCase benchmark) => RunMode.ExtraRun;

        public void Handle(HostSignal signal, DiagnoserActionParameters parameters)
        {
            if (signal == HostSignal.BeforeAnythingElse)
                BeforeAnythingElse(parameters);
            else if (signal == HostSignal.BeforeActualRun)
                BeforeMainRun(parameters);
            else if (signal == HostSignal.AfterAll)
                BeforeGlobalCleanup(parameters);
        }

        public void ProcessResults(DiagnoserResults results)
        {
        }

        public IEnumerable<IAnalyser> Analysers => Array.Empty<IAnalyser>();
    }

    public class MethodTimeFractionColumn : IColumn
    {
        private readonly string displayName;
        private readonly IDictionary<BenchmarkCase, float[]> dict;
        private readonly int mIndex;

        public MethodTimeFractionColumn(string displayName, IDictionary<BenchmarkCase, float[]> dict, int mIndex)
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

        public string GetValue(Summary summary, BenchmarkCase benchmark) =>
            GetValue(summary, benchmark, new SummaryStyle { PrintUnitsInContent = true });

        public string GetValue(Summary summary, BenchmarkCase benchmark, ISummaryStyle style) =>
            dict.TryGetValue(benchmark, out var times) ?
            $"{times[mIndex] * 100f}{( style.PrintUnitsInContent ? "%" : "")}" :
            "-";

        public bool IsAvailable(Summary summary) => true;

        public bool IsDefault(Summary summary, BenchmarkCase benchmark) => !dict.ContainsKey(benchmark);
    }
    public class FileNameColumn : IColumn
    {
        private readonly Dictionary<BenchmarkCase, string> fileName;

        public FileNameColumn(string colName, string id, Dictionary<BenchmarkCase, string> fileName)
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

        public string GetValue(Summary summary, BenchmarkCase benchmark)
        {
            if (fileName.TryGetValue(benchmark, out var val) && File.Exists(val))
            {
                return val;
            }
            return "-";
        }

        public string GetValue(Summary summary, BenchmarkCase benchmark, ISummaryStyle style)
        {
            return GetValue(summary, benchmark);
        }

        public bool IsAvailable(Summary summary)
        {
            return true;
        }

        public bool IsDefault(Summary summary, BenchmarkCase benchmark)
        {
            return false;
        }
    }
}
