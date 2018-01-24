using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
            if (!perfProcess.HasExited)
                Process.Start("kill", $"-s SIGINT {perfProcess.Id}");
            if (!perfProcess.WaitForExit(2_000))
                throw new Exception("perf did not exit");
        }

        public static void ExecMapgen(Process process)
        {
            Console.WriteLine($"mapgen process {process.Id}");
            Process.Start("python2", $"./dotnet-mapgen-v2.py generate {process.Id}").WaitForExit();
            Process.Start("python2", $"./dotnet-mapgen-v2.py merge {process.Id}").WaitForExit();
        }

        public static CollectionHandler StartCollection(string outFile, Process process, bool removeOutFile = true, string exportStacksFile = null, bool allowDotnetMapgen = true)
        {
            // touch the file
            // File.Create(outFile).Dispose();

            var args = new ProcessStartInfo("perf", $"record -o {outFile} --pid {process.Id} -F 300 -g");
            var perfProcess = Process.Start(args);
            if (perfProcess.WaitForExit(100))
                throw new Exception("perf has exited ;(");
            return new CollectionHandler(perfProcess, process, outFile, removeOutFile, exportStacksFile, allowDotnetMapgen);
        }

        public class CollectionHandler
        {
            private readonly Process perfProcess;
            private readonly Process process;
            private readonly bool removeOutFile;
            private readonly string exportStacksFile;
            private readonly string outFile;
            private readonly bool allowDotnetMapgen;

            public CollectionHandler(Process perfProcess, Process process, string outFile, bool removeOutFile = true, string exportStacksFile = null, bool allowDotnetMapgen = true)
            {
                this.allowDotnetMapgen = allowDotnetMapgen;
                this.perfProcess = perfProcess;
                this.process = process;
                this.removeOutFile = removeOutFile;
                this.exportStacksFile = exportStacksFile;
                this.outFile = Path.GetFullPath(outFile);
            }
            public Func<(string[] stack, int number)[]> StopAndLazyMerge()
            {
                StopCollection(this.perfProcess);
                var pid = process.Id;
                return () => {
                    try
                    {
                        var stacks = LoadPerfStacks(outFile).ToArray();
                        // try { File.Delete(outFile); } catch {};
                        return stacks;
                    }
                    finally
                    {
                        try
                        {
                            if (this.removeOutFile && File.Exists(this.outFile))
                            {
                                File.Delete(this.outFile);
                                File.Delete($"/tmp/perf-{this.process.Id}.map");
                                File.Delete($"/tmp/perfinfo-{this.process.Id}.map");
                            }
                        }
                        catch { }
                    }
                };
            }

            private TextWriter OpenStacksFile()
            {
                if (this.exportStacksFile == null) return null;
                Stream stream = null;
                try
                {
                    stream = File.Create(this.exportStacksFile);
                    if (this.exportStacksFile.EndsWith(".gz"))
                        stream = new GZipStream(stream, CompressionLevel.Optimal);
                    return new StreamWriter(stream);
                }
                catch
                {
                    stream?.Dispose();
                    throw;
                }
            }

            private IEnumerable<(string[], int)> LoadPerfStacks(string file)
            {
                var args = new ProcessStartInfo("bash");
                args.RedirectStandardOutput = true;
                args.RedirectStandardInput = true;
                var proc = Process.Start(args);
                using (var input = proc.StandardInput)
                {
                    input.WriteLine($"perf script -i {file} | FlameGraph/stackcollapse-perf.pl --all");
                }
                var output = proc.StandardOutput;
                string line = null;
                using (var export = OpenStacksFile())
                {
                    while ((line = output.ReadLine()) != null)
                    {
                        export?.WriteLine(line);
                        var lastSpace = line.LastIndexOf(' ');
                        if (lastSpace < 0) continue;
                        var number = int.Parse(line.Substring(lastSpace));
                        var stacks = line.Split(';');
                        stacks[stacks.Length - 1] = stacks[stacks.Length - 1].Remove(stacks[stacks.Length - 1].Length - (line.Length - lastSpace));
                        yield return (stacks, number);
                    }
                    if (export != null)
                        Console.WriteLine($"CPU Stacks exported to {this.exportStacksFile}");
                }
            }
        }
    }
}
