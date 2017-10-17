using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotVVM.Benchmarks
{
    public class PerfHandler
    {
        private static object locker = new object();

        static void StopCollection(Process perfProcess)
        {
            // if (!perfProcess.HasExited)
            //     Process.Start("sudo", $"kill -s SIGINT {perfProcess.Id}");
            if (!perfProcess.WaitForExit(2000))
                throw new Exception("perf did not exit");
        }

        public static CollectionHandler StartCollection(string outFile, Process process)
        {
            // touch the file
            // File.Create(outFile).Dispose();

            var args = new ProcessStartInfo("perf", $"record -o {outFile} --pid {process.Id} -F 300 -g");
            var perfProcess = Process.Start(args);
            if (perfProcess.WaitForExit(100))
                throw new Exception("perf has exited ;(");
            return new CollectionHandler(perfProcess, process, outFile);
        }

        public class CollectionHandler
        {
            private readonly Process perfProcess;
            private readonly Process process;
            private readonly string outFile;

            public CollectionHandler(Process perfProcess, Process process, string outFile)
            {
                this.perfProcess = perfProcess;
                this.process = process;
                this.outFile = Path.GetFullPath(outFile);
            }
            public Func<(string[] stack, int number)[]> StopAndLazyMerge()
            {
                StopCollection(this.perfProcess);
                var pid = process.Id;
                return () => {
                    var stacks = LoadPerfStacks(outFile).ToArray();
                    // try { File.Delete(outFile); } catch {};
                    return stacks;
                };
            }

            private IEnumerable<(string[], int)> LoadPerfStacks(string file)
            {
                var args = new ProcessStartInfo("bash");
                args.RedirectStandardOutput = true;
                args.RedirectStandardInput = true;
                var proc = Process.Start(args);
                using(var input = proc.StandardInput)
                {
                    input.WriteLine($"perf script -i {file} | FlameGraph/stackcollapse-perf.pl --all");
                }
                var output = proc.StandardOutput;
                string line = null;
                while ((line = output.ReadLine()) != null)
                {
                    var lastSpace = line.LastIndexOf(' ');
                    if (lastSpace < 0) continue;
                    var number = int.Parse(line.Substring(lastSpace));
                    var stacks = line.Split(';');
                    stacks[stacks.Length - 1] = stacks[stacks.Length - 1].Remove(stacks[stacks.Length - 1].Length - (line.Length - lastSpace));
                    yield return (stacks, number);
                }
            }
        }
    }
}
