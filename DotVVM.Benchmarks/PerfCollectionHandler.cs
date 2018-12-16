using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Medallion.Shell;

namespace DotVVM.Benchmarks
{
    public class PerfHandler
    {
        private static object locker = new object();

        static void StopCollection(Command perfProcess)
        {
            if (!perfProcess.Process.HasExited)
                Command.Run("kill", new [] {"-s", "SIGINT", perfProcess.ProcessId.ToString()}).Wait();
            if (!perfProcess.Process.WaitForExit(20_000))
                throw new Exception("perf did not exit");
            perfProcess.Process.Dispose();
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

            var perfCmd = Command.Run("perf", new [] { "record", "-o", outFile, "--pid", process.Id.ToString(), "-F", "300", "-g" }, opt => { opt.DisposeOnExit(false); });

            if (perfCmd.Process.WaitForExit(100))
                throw new Exception("perf has exited ;(");
            return new CollectionHandler(perfCmd, process, outFile, removeOutFile, exportStacksFile, allowDotnetMapgen);
        }

        public class CollectionHandler
        {
            private readonly Command perfProcess;
            private readonly Process process;
            private readonly bool removeOutFile;
            private readonly string exportStacksFile;
            private readonly string outFile;
            private readonly bool allowDotnetMapgen;

            public CollectionHandler(Command perfProcess, Process process, string outFile, bool removeOutFile = true, string exportStacksFile = null, bool allowDotnetMapgen = true)
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
                var command =
                    Command.Run("perf", new [] { "script", "-i", file }).PipeTo(
                    Command.Run("FlameGraph/stackcollapse-perf.pl", "--all"));

                var output = command.StandardOutput;

                string line = null;
                int lineCount = 0;
                using (var export = OpenStacksFile())
                {
                    while ((line = output.ReadLine()) != null)
                    {
                        lineCount++;
                        export?.WriteLine(line);
                        var lastSpace = line.LastIndexOf(' ');
                        if (lastSpace < 0) continue;
                        var number = int.Parse(line.Substring(lastSpace));
                        var stacks = line.Split(';');
                        stacks[stacks.Length - 1] = stacks[stacks.Length - 1].Remove(stacks[stacks.Length - 1].Length - (line.Length - lastSpace));
                        yield return (stacks, number);
                    }
                }
                if (this.exportStacksFile != null && lineCount > 0)
                    Console.WriteLine($"CPU Stacks exported to {this.exportStacksFile}");
                if (this.exportStacksFile != null && lineCount == 0)
                {
                    Console.WriteLine($"Export of stacks failed - there was nothing");
                    try { File.Delete(this.exportStacksFile); } catch { Console.WriteLine(" ... and delete of the export file failed."); }
                }
            }
        }
    }
}
