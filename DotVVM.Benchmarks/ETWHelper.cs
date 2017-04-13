using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace DotVVM.Benchmarks
{
    public static class ETWHelper
    {
        public static (long totalProcessed, long thisProcess, long thisFunction) FilterTraceByPID(string sourceFile, string destinationFile, int pid)
        {
            using (var source = new ETWReloggerTraceEventSource(sourceFile, TraceEventSourceType.FileOnly, destinationFile))
            {
                long count = 0;
                long written = 0;
                source.AllEvents += (e) => {

                    count++;
                    //if (count % 65536 == 0) logger?.Invoke((count, written, written));
                    if (e.ProcessID == pid || e.ProcessID <= 1)
                    {
                        source.WriteEvent(e);
                        written++;
                    }
                };
                source.Process();
                return (count, written, written);
            }
        }

        public static (TraceLog log, string etlxFile) GetTraceLog(string fileName)
        {
            string etlxFile = Path.ChangeExtension(fileName, ".etlx");
            var li = System.Diagnostics.Trace.Listeners.Cast<System.Diagnostics.TraceListener>().ToArray();
            for (int i = 0; i < System.Diagnostics.Trace.Listeners.Count; i++)
            {
                System.Diagnostics.Trace.Listeners.RemoveAt(0);
            }
            var dllWhiteList = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name), StringComparer.OrdinalIgnoreCase);
            var log = TraceLog.OpenOrConvert(fileName, new TraceLogOptions { ShouldResolveSymbols = name => dllWhiteList.Contains(name) });
            System.Diagnostics.Trace.Listeners.AddRange(li);
            //TraceLog.CreateFromEventTraceLogFile()
            return (log, etlxFile);
        }

        public static Dictionary<string, CallTreeItem> GetCallTree(StackSource stacks, out float timePerStack)
        {
            var callTree = new Dictionary<string, CallTreeItem>(StringComparer.OrdinalIgnoreCase);
            void AddRecursively(StackSourceCallStackIndex index, int depth = 0, CallTreeItem p = null)
            {
                var name = stacks.GetFrameName(stacks.GetFrameIndex(index), false);
                if (name == "BROKEN") return;
                var caller = stacks.GetCallerIndex(index);
                if (!callTree.TryGetValue(name, out var item)) callTree.Add(name, item = new CallTreeItem(name));
                item.IncSamples++;
                if (depth == 0) item.Samples++;
                else item.AddCallee(p);
                p?.AddCaller(item);

                if (caller != StackSourceCallStackIndex.Invalid) AddRecursively(caller, depth + 1, item);
            }
            var metric = float.NaN;
            stacks.ForEach(stack => {
                if (float.IsNaN(metric)) metric = stack.Metric;
                if (metric != stack.Metric) throw new Exception();
                if (stack.Count != 1) throw new Exception();
                AddRecursively(stack.StackIndex);
            });
            timePerStack = metric;
            return callTree;
        }

        public static IEnumerable<float> ComputeTimeFractions(Dictionary<string, CallTreeItem> callTree, string[] methodNames)
        {
            var baseLine = callTree[methodNames.First()];
            while (baseLine.Callers.Count == 1)
                baseLine = baseLine.Callers.Single().Key;

            foreach (var m in methodNames)
            {
                if (callTree.TryGetValue(m, out var cti))
                {
                    yield return (float)cti.IncSamples / (float)baseLine.IncSamples;
                }
                else
                    yield return 0f;
            }
        }


        public class CallTreeItem
        {
            public CallTreeItem(string name)
            {
                this.Name = name;
            }

            public string Name { get; }
            public ulong Samples { get; set; }
            public ulong IncSamples { get; set; }
            private Dictionary<CallTreeItem, int> _callees = null;
            public IEnumerable<CallTreeItem> Callees => _callees?.Keys ?? Enumerable.Empty<CallTreeItem>();
            public IEnumerable<KeyValuePair<CallTreeItem, int>> CalleesWithSampleCounts => _callees ?? Enumerable.Empty<KeyValuePair<CallTreeItem, int>>();
            public void AddCallee(CallTreeItem cti)
            {
                (_callees ?? (_callees = new Dictionary<CallTreeItem, int>(2))).TryGetValue(cti, out int current);
                _callees[cti] = current + 1;
            }

            public Dictionary<CallTreeItem, int> Callers { get; } = new Dictionary<CallTreeItem, int>(2);
            public void AddCaller(CallTreeItem cti)
            {
                Callers.TryGetValue(cti, out int current);
                Callers[cti] = current + 1;
            }

        }
    }
}
