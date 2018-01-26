using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using DotVVM.Framework.Utils;
using Newtonsoft.Json.Linq;

namespace DotVVM.Benchmarks
{
    public class BrowserTimeOptions
    {
        /// If the site should be loaded before testing (to warm up caches)
        public bool Preload = false;
        /// --sitespeed
        public bool Sitespeed = true;
        public string Browser = "chrome";
        public int Iterations = 10;
        public string[] GetArgs(string testUrl)
        {
            var args = new List<string>();
            if (Sitespeed)
                args.Add("--sitespeed");
            args.Add("--browser");
            args.Add(Browser);
            args.Add("--iterations");
            args.Add(Iterations.ToString());
            if (Preload)
            {
                args.Add("--preUrl");
                args.Add(testUrl);
            }
            args.Add(testUrl);
            return args.ToArray();
        }
    }

    public class BrowserTimeResults
    {
        public string WroteDataTo;
        public Dictionary<string, double> MeasuredValues = new Dictionary<string, double>();
    }

    public class FrontendBenchmarker
    {
        private const string browserTimeDockerImage = "sitespeedio/browsertime:2.1.7";
        private static IEnumerable<string> RunBenchmark(BrowserTimeOptions options, string resultsDirectory, string url)
        {
            var args = options.GetArgs(url);
            var processStartInfo = new ProcessStartInfo("sudo", $"docker run --shm-size=1g --rm -v \"{resultsDirectory}\":/browsertime {browserTimeDockerImage} {string.Join(" ", args)}");
            Console.WriteLine($"// running browsertime - {processStartInfo.Arguments}");
            processStartInfo.RedirectStandardOutput = true;
            var process = Process.Start(processStartInfo);

            string line;
            while ((line = process.StandardOutput.ReadLine()) != null)
            {
                yield return line;
            }
            if (!process.WaitForExit(2_000))
                throw new Exception("BrowserTime: Process has not exited, but out stream is closed");
            if (process.ExitCode != 0)
                throw new Exception($"BrowserTIme: Exit code = {process.ExitCode}");
        }

        private static BrowserTimeResults ParseResults(string resultsDirectory, IEnumerable<string> lines, bool removeDir)
        {
            var results = new BrowserTimeResults();
            foreach (var line in lines)
            {
                Console.WriteLine(line);
                var lineWithoutTime = line.Substring(line.IndexOf(']') + 1).Trim();
                if (lineWithoutTime.StartsWith("Wrote data to "))
                    results.WroteDataTo = lineWithoutTime.Substring("Wrote data to ".Length).Trim();
                else if ((uint)lineWithoutTime.IndexOf(" requests, ") < 5)
                {
                    var segments = lineWithoutTime.Split(',').Select(s => s.Trim()).ToArray();
                    var transfered = segments.FirstOrDefault(s => s.EndsWith(" kb"));
                    if (transfered != null)
                    {
                        results.MeasuredValues.Add("transfered", double.Parse(transfered.Remove(transfered.Length - " kb".Length)) * 1000);
                    }
                }
            }
            if (results.WroteDataTo != null && File.Exists(Path.Combine(resultsDirectory, results.WroteDataTo, "browsertime.json")))
            {
                var json = JObject.Parse(File.ReadAllText(Path.Combine(resultsDirectory, results.WroteDataTo, "browsertime.json")));
                var stats = json["statistics"];
                stats.Replace(new JValue(0)); // HACK: Cannot add or remove items from Newtonsoft.Json.Linq.JProperty.
                foreach (var stat in stats.SelectTokens("..median"))
                {
                    results.MeasuredValues.Add(stat.Parent.Parent.Path, (double)stat);
                }
                if (removeDir)
                {
                    try { Directory.Delete(Path.Combine(resultsDirectory, results.WroteDataTo), recursive: true); } catch { }
                }
            }
            return results;
        }

        private static string GetLocalHostname()
        {
            return System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()).First(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToString();
        }

        public static void BenchmarkApplication<TAppLauncher>(
            BrowserTimeOptions browserTimeOptions,
            string resultDirectory
        )
            where TAppLauncher : IApplicationLauncher, new()
        {
            resultDirectory = Path.GetFullPath(resultDirectory);
            var (builder, configuration) = DotvvmTestHost.InitializeBuilder<TAppLauncher>(DotvvmSamplesBenchmarker<TAppLauncher>.GetRootPath());
            var host = builder
                .UseUrls("http://*:5004")
                .UseKestrel()
                .Build();
            var hostCancel = new CancellationTokenSource();
            var hostTask = host.RunAsync(hostCancel.Token);

            Debug.Assert(configuration.Value != null);
            var hostName = GetLocalHostname();
            Console.WriteLine($"// hostname = {hostName}");
            var urls = DotvvmSamplesBenchmarker<TAppLauncher>.GetTestRoutes(configuration.Value);
            var allResults = new List<(string, BrowserTimeResults)>();
            foreach (var url in urls)
            {
                var absUrl = $"http://{hostName}:5004/{url.TrimStart('/')}";
                Console.WriteLine($"// Benchmarking page load time - {url}");

                try
                {
                    Enumerable.Range(0, 40)
                        .Select(_ => new HttpClient().GetStringAsync(absUrl))
                        .ToArray()
                        .Apply(Task.WhenAll)
                        .Wait();
                    var results = ParseResults(resultDirectory, RunBenchmark(browserTimeOptions, resultDirectory, absUrl), removeDir: true);
                    allResults.Add((url, results));
                    Console.WriteLine($"// Done ({string.Join(", ", results.MeasuredValues.Select(k => k.Key + ": " + k.Value))})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Benchmarking failed: {ex}");
                }
            }
        }
    }
}