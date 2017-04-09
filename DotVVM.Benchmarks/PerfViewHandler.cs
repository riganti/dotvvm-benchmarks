using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PerfView;

namespace DotVVM.Benchmarks
{
    public class PerfViewHandler
    {
        private static object locker = new object();
        private static AppDomain perfViewDomain;

        public static AppDomain GetPerfViewDomain()
        {
            if (perfViewDomain != null) return perfViewDomain;
            lock (locker)
            {
                if (perfViewDomain != null) return perfViewDomain;
                return perfViewDomain = CreatePerfViewDomain();
            }
        }

        static AppDomain CreatePerfViewDomain()
        {
            var d = AppDomain.CreateDomain("PerfViewDomain");
            d.DoCallBack(PerfViewDomainLaunch);
            return d;
        }


        static void StopCollection(int collectionHandle)
        {
            var d = GetPerfViewDomain();
            lock (locker)
            {
                d.SetData("collectionHandle", collectionHandle);
                d.DoCallBack(PerfViewDomainMethods.StopCollection);
            }
        }
        public static CollectionHandler StartCollection(string outFile, string processName)
        {
            var d = GetPerfViewDomain();
            lock (locker)
            {
                d.SetData("outFile", outFile);
                d.SetData("processName", processName);

                d.DoCallBack(PerfViewDomainMethods.RunCollection);

                var handle = (int)d.GetData("collectionHandle");
                return new CollectionHandler(handle);
            }
        }

        public class CollectionHandler
        {
            private readonly int collectionHandle;

            public CollectionHandler(int collectionHandle)
            {
                this.collectionHandle = collectionHandle;
            }
            public void Stop()
            {
                StopCollection(collectionHandle);
            }
        }

        public static void PerfViewDomainLaunch()
        {
            PerfView.App.Unpack();
            if (!PerfView.App.IsElevated) throw new Exception("Application must be elevated.");
        }
    }

    static class PerfViewDomainMethods
    {   
        static CommandLineArgs CreateArgs(string outFile, string processName = null)
        {
            var a = App.CommandLineArgs = new CommandLineArgs();
            a.ParseArgs(new[] { "/NoGui" });
            a.RestartingToElevelate = ""; // this should prevent PerfView from trying to elevate itself
            a.Zip = true;
            a.Merge = true;
            a.InMemoryCircularBuffer = false;
            a.DotNetAllocSampled = true;
            a.CpuSampleMSec = 0.125f;
            //a.StackCompression = true;

            a.DataFile = outFile;
            a.Process = processName ?? a.Process;
            a.NoNGenRundown = true;
            a.TrustPdbs = true;
            a.UnsafePDBMatch = true;
            return a;
        }

        private static ConcurrentDictionary<int, (CommandProcessor, CommandLineArgs)> collectionHandles = new ConcurrentDictionary<int, (CommandProcessor, CommandLineArgs)>();
        private static int collectionHandleIdCtr;
        public static void RunCollection()
        {
            var path = (string)AppDomain.CurrentDomain.GetData("outFile");
            var processName = (string)AppDomain.CurrentDomain.GetData("processName");

            var commandProcessor = App.CommandProcessor = new CommandProcessor() { LogFile = Console.Out };
            var commandArgs = CreateArgs(path, processName);
            commandProcessor.Start(commandArgs);

            var handle = Interlocked.Increment(ref collectionHandleIdCtr);
            collectionHandles[handle] = (commandProcessor, commandArgs);
            AppDomain.CurrentDomain.SetData("collectionHandle", handle);
        }

        public static void StopCollection()
        {
            var handle = (int)AppDomain.CurrentDomain.GetData("collectionHandle");
            var (proc, args) = collectionHandles[handle];
            collectionHandles.TryRemove(handle, out var _);
            proc.Stop(args);
        }
    }
}
