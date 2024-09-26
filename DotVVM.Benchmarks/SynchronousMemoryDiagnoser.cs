/////////////////////////////////////////////////////////////////////////////////////////////////
//
// SynchContext sample codes
// Copyright (c) 2016 Kouji Matsui (@kekyo2)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//	http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
/////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.Results;
using BenchmarkDotNet.Validators;

namespace DotVVM.Benchmarks
{
    public class SynchronousMemoryDiagnoser : IDiagnoser
    {
        private static bool isProfiling = System.IO.File.Exists(MarkerFileName);
        public static T RunTask<T>(Func<Task<T>> task)
        {
            if (isProfiling)
            {
                return QueueSynchronizationContext.RunTask(task);
            }
            else
            {
                return task().GetAwaiter().GetResult();
            }
        }

        private const string MarkerFileName = "/tmp/benchmarks-profile-memory-69f55986-490d-49e9-be2d-3f07f88c4da1-marker";

        public SynchronousMemoryDiagnoser()
        {
            if (System.IO.File.Exists(MarkerFileName))
            {
                System.IO.File.Delete(MarkerFileName);
            }
        }

        private const string DiagnoserId = nameof(SynchronousMemoryDiagnoser);

        public static readonly SynchronousMemoryDiagnoser Default = new SynchronousMemoryDiagnoser();

        public RunMode GetRunMode(BenchmarkCase benchmarkCase) => RunMode.ExtraRun;

        public IEnumerable<string> Ids => new[] { DiagnoserId };

        public IEnumerable<IExporter> Exporters => Array.Empty<IExporter>();

        public IEnumerable<IAnalyser> Analysers => Array.Empty<IAnalyser>();

        public void Handle(HostSignal signal, DiagnoserActionParameters parameters)
        {
            if (signal == HostSignal.AfterProcessExit)
            {
                System.IO.File.Delete(MarkerFileName);
            }
            else if (signal == HostSignal.BeforeProcessStart)
            {
                System.IO.File.CreateText(MarkerFileName).Dispose();
            }
        }

        Dictionary<BenchmarkCase, GcStats> savedStats = new Dictionary<BenchmarkCase, GcStats>();

        public GcStats? FindGCStats(BenchmarkCase benchmark) => savedStats.TryGetValue(benchmark, out var result) ? (GcStats?)result : null;

        public IEnumerable<Metric> ProcessResults(DiagnoserResults diagnoserResults)
        {
            var gcStatsLine = InterceptingExecutor.LastExecResult.Results.LastOrDefault(line => line.StartsWith("GC"));
            if (gcStatsLine is null)
                Console.WriteLine(string.Join("\n", InterceptingExecutor.LastExecResult.Results));
            var gcStats = GcStats.Parse(gcStatsLine);
            savedStats[diagnoserResults.BenchmarkCase] = gcStats;

            IMetricDescriptor mapDescriptor(IMetricDescriptor d) =>
                new GenericMetricDescriptor("X" + d.Id, "X" + d.DisplayName, d.UnitType, d.Legend, d.Unit, d.TheGreaterTheBetter, d.NumberFormat);

            var config = MemoryDiagnoser.Default.Config;
            // copy paste from MemoryDiagnoser.ProcessResults
            if (diagnoserResults.GcStats.Gen0Collections > 0 && config.DisplayGenColumns)
            {
                yield return new Metric(mapDescriptor(GarbageCollectionsMetricDescriptor.Gen0), (double)gcStats.Gen0Collections / (double)gcStats.TotalOperations * 1000.0);
            }
            if (diagnoserResults.GcStats.Gen1Collections > 0 && config.DisplayGenColumns)
            {
                yield return new Metric(mapDescriptor(GarbageCollectionsMetricDescriptor.Gen1), (double)gcStats.Gen1Collections / (double)gcStats.TotalOperations * 1000.0);
            }
            if (diagnoserResults.GcStats.Gen2Collections > 0 && config.DisplayGenColumns)
            {
                yield return new Metric(mapDescriptor(GarbageCollectionsMetricDescriptor.Gen2), (double)gcStats.Gen2Collections / (double)gcStats.TotalOperations * 1000.0);
            }
            yield return new Metric(mapDescriptor(AllocatedMemoryMetricDescriptor.Instance), gcStats.GetBytesAllocatedPerOperation(diagnoserResults.BenchmarkCase) ?? 0);
        }

        public void DisplayResults(ILogger logger)
        {
        }

        public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
        {
            return Enumerable.Empty<ValidationError>();
        }

        private class AllocatedMemoryMetricDescriptor : IMetricDescriptor
        {
            internal static readonly IMetricDescriptor Instance = new AllocatedMemoryMetricDescriptor();
            public string Id => "Allocated Memory";
            public string DisplayName => "Allocated Memory/Op";
            public string Legend => "Allocated memory per single operation (managed only, inclusive, 1KB = 1024B)";
            public string NumberFormat => "N0";
            public UnitType UnitType => UnitType.Size;
            public string Unit => SizeUnit.B.Name;
            public bool TheGreaterTheBetter => false;

            public int PriorityInCategory => 0;

            public bool GetIsAvailable(Metric metric)
            {
                return metric.Value > 0;
            }
        }

        private class GarbageCollectionsMetricDescriptor : IMetricDescriptor
        {
            internal static readonly IMetricDescriptor Gen0 = new GarbageCollectionsMetricDescriptor(0);
            internal static readonly IMetricDescriptor Gen1 = new GarbageCollectionsMetricDescriptor(1);
            internal static readonly IMetricDescriptor Gen2 = new GarbageCollectionsMetricDescriptor(2);

            private GarbageCollectionsMetricDescriptor(int generationId)
            {
                Id = $"Gen{generationId}Collects";
                DisplayName = $"Gen {generationId}/1k Op";
                Legend = $"GC Generation {generationId} collects per 1k Operations";
            }

            public string Id { get; }
            public string DisplayName { get; }
            public string Legend { get; }
            public string NumberFormat => "#0.0000";
            public UnitType UnitType => UnitType.Dimensionless;
            public string Unit => "Count";
            public bool TheGreaterTheBetter => false;

            public int PriorityInCategory => 0;

            public bool GetIsAvailable(Metric metric)
            {
                return metric.Value > 0;
            }
        }
}
    /// <summary>
    /// Custom synchronization context implementation using BlockingCollection.
    /// </summary>
    public sealed class QueueSynchronizationContext : SynchronizationContext
    {
        private struct ContinuationInformation
        {
            public SendOrPostCallback Continuation;
            public object State;
        }

        /// <summary>
        /// Continuation queue.
        /// </summary>
        private readonly BlockingCollection<ContinuationInformation> queue =
            new BlockingCollection<ContinuationInformation>();

        /// <summary>
        /// This synchronization context bound thread id.
        /// </summary>
        private readonly int targetThreadId = Thread.CurrentThread.ManagedThreadId;

        /// <summary>
        /// Number of recursive posts.
        /// </summary>
        private int recursiveCount = 0;

        /// <summary>
        /// Constructor.
        /// </summary>
        public QueueSynchronizationContext()
        {
        }

        /// <summary>
        /// Copy instance.
        /// </summary>
        /// <returns>Copied instance.</returns>
        public override SynchronizationContext CreateCopy()
        {
            return new QueueSynchronizationContext();
        }

        /// <summary>
        /// Send continuation into synchronization context.
        /// </summary>
        /// <param name="continuation">Continuation callback delegate.</param>
        /// <param name="state">Continuation argument.</param>
        public override void Send(SendOrPostCallback continuation, object state)
        {
            this.Post(continuation, state);
        }

        /// <summary>
        /// Post continuation into synchronization context.
        /// </summary>
        /// <param name="continuation">Continuation callback delegate.</param>
        /// <param name="state">Continuation argument.</param>
        public override void Post(SendOrPostCallback continuation, object state)
        {
            // If current thread id is target thread id:
            var currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId == targetThreadId)
            {
                // HACK: If current thread is already target thread, invoke continuation directly.
                //   But if continuation has invokeing Post/Send recursive, cause stack overflow.
                //   We can fix this problem by simple solution: Continuation invoke every post into queue,
                //   but performance will be lost.
                //   This counter uses post for scattering (each 50 times).
                if (recursiveCount < 50)
                {
                    recursiveCount++;

                    // Invoke continuation on current thread is better performance.
                    continuation(state);

                    recursiveCount--;
                    return;
                }
            }


            // Add continuation information into queue.
            queue.Add(new ContinuationInformation { Continuation = continuation, State = state });
        }

        /// <summary>
        /// Execute message queue.
        /// </summary>
        public void Run()
        {
            this.Run(null);
        }

        /// <summary>
        /// Execute message queue.
        /// </summary>
        /// <param name="task">Completion awaiting task</param>
        public void Run(Task task)
        {
            // Run only target thread.
            var currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId != targetThreadId)
            {
                throw new InvalidOperationException();
            }

            // Schedule task completion for abort queue consumer.
            task?.ContinueWith(_ => queue.CompleteAdding());

            // Run queue consumer.
            foreach (var continuationInformation in queue.GetConsumingEnumerable())
            {
                // Invoke continuation.
                continuationInformation.Continuation(continuationInformation.State);
            }
        }

        public static void RunTask(Func<Task> task)
        {
            var last = SynchronizationContext.Current;
            var c = new QueueSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(c);
            var taskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            var runningTask = Task<Task>.Factory.StartNew(task, CancellationToken.None, TaskCreationOptions.None, taskScheduler).Unwrap();
            c.Run(runningTask);
            SynchronizationContext.SetSynchronizationContext(last);
        }
        public static T RunTask<T>(Func<Task<T>> task)
        {
            Task<T> r = null;
            RunTask(() => (Task)(r = task()));
            return r.Result;
        }
    }
}
