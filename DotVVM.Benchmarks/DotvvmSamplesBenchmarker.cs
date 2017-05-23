using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Parameters;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using DotVVM.Framework.Configuration;
using Newtonsoft.Json.Linq;

namespace DotVVM.Benchmarks
{
    public class DotvvmSamplesBenchmarker<TAppLauncher>
            where TAppLauncher : IApplicationLauncher, new()
    {
        public static IEnumerable<Benchmark> BenchmarkSamples(IConfig config, bool getRequests = true, bool postRequests = true)
        {
            var host = CreateSamplesTestHost();
            var getBenchmarks = getRequests ? AllGetBenchmarks(config, host).ToArray() : new Benchmark[0];
            var postBenchmarks = postRequests ? AllPostBenchmarks(config, host).ToArray() : new Benchmark[0];

            return getBenchmarks.Concat(postBenchmarks).ToArray();
        }

        public static IEnumerable<Benchmark> BenchmarkMvcSamples(IConfig config, bool getRequests = true, bool postRequests = true)
        {
            var host = CreateSamplesTestHost();
            var bb = AllMvcBenchmarks(config, host).ToArray();
            return bb;
        }

        public static DotvvmTestHost CreateSamplesTestHost()
        {
            var currentPath = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(currentPath, "DotVVM.Benchmarks.sln"))) currentPath = Path.GetDirectoryName(currentPath);
            return DotvvmTestHost.Create<TAppLauncher>(currentPath);
        }

        public static IEnumerable<Benchmark> AllMvcBenchmarks(IConfig config, DotvvmTestHost testHost)
        {
            IEnumerable<Benchmark> createMvcBenchmarks(Benchmark b)
            {
                var urls = new[] { "/Home/Index" };
                var definiton = new ParameterDefinition(nameof(DotvvmGetBenchmarks<TAppLauncher>.Url), false, new object[] { });
                foreach (var url in urls)
                {
                    new DotvvmGetBenchmarks<TAppLauncher> { Url = url }.TestDotvvmRequest();
                    yield return new Benchmark(b.Target, b.Job, new ParameterInstances(new[] { new ParameterInstance(definiton, url) }));
                }
            }
            return BenchmarkConverter.TypeToBenchmarks(typeof(DotvvmGetBenchmarks<TAppLauncher>), config)
                .SelectMany(createMvcBenchmarks);
        }

        private static IEnumerable<Benchmark> AllGetBenchmarks(IConfig config, DotvvmTestHost testHost)
        {
            return BenchmarkConverter.TypeToBenchmarks(typeof(DotvvmGetBenchmarks<TAppLauncher>), config)
                .SelectMany(b => CreateBenchmarks(b, testHost, testHost.Configuration));
        }

        private static IEnumerable<Benchmark> AllPostBenchmarks(IConfig config, DotvvmTestHost testHost)
        {
            if (Directory.Exists("testViewModels")) Directory.Delete("testViewModels", true);
            Directory.CreateDirectory("testViewModels");
            return BenchmarkConverter.TypeToBenchmarks(typeof(DotvvmPostbackBenchmarks<TAppLauncher>), config)
                .SelectMany(b => CreatePostbackBenchmarks(b, testHost, testHost.Configuration));
        }

        private static IEnumerable<string> GetTestRoutes(DotvvmConfiguration config) =>
            config.RouteTable
            // TODO support parameters somehow
            .Where(r => r.ParameterNames.Count() == 0)
            .Where(r => !r.RouteName.Contains("Auth") && !r.RouteName.Contains("SPARedirect") && !r.RouteName.Contains("Error")) // Auth samples cause problems, because thei viewModels are not loaded
            .Select(r => r.BuildUrl().TrimStart('~'));

        public static IEnumerable<Benchmark> CreateBenchmarks(Benchmark b, DotvvmTestHost host, DotvvmConfiguration config)
        {
            var urls = GetTestRoutes(config);
            var definiton = new ParameterDefinition(nameof(DotvvmGetBenchmarks<TAppLauncher>.Url), false, new object[] { });
            foreach (var url in urls)
            {
                yield return new Benchmark(b.Target, b.Job, new ParameterInstances(new[] { new ParameterInstance(definiton, url) }));
            }
        }

        private static Regex postbackScriptRegex = new Regex(@".*dotvvm\.postback(script\(['""](?<CommandId>.+)['""]\))?[^a-zA-Z0-9'""]*['""](?<PageSpace>.*)['""],[^a-zA-Z0-9'""]*this,[^a-zA-Z0-9'""]*(?<TargetPath>\[.*\]),[^a-zA-Z0-9'""]*(['""](?<CommandId>.+)['""],[^a-zA-Z0-9'""]*)?['""](?<ControlId>.*)['""][^a-zA-Z0-9'""]*(true|false)[^a-zA-Z0-9'""]*['""](?<ValidationPath>.+)['""],.*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static IEnumerable<(string json, string name)> FindPostbacks(string html)
        {
            if (html.IndexOf("dotvvm.postback", StringComparison.OrdinalIgnoreCase) < 0) yield break;
            var dom = new AngleSharp.Parser.Html.HtmlParser().Parse(html);
            var vm = new Lazy<JObject>(() => JObject.Parse(dom.GetElementById("__dot_viewmodel_root").GetAttribute("value")));
            foreach (var element in dom.All)
            {
                foreach (var attribute in element.Attributes)
                {
                    if (attribute.Value.IndexOf("dotvvm.postback", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var match = postbackScriptRegex.Match(attribute.Value);
                    if (!match.Success) continue;
                    var commandId = match.Groups["CommandId"].Value;
                    var controlId = match.Groups["ControlId"].Value;
                    var pageSpace = match.Groups["PageSpace"].Value;
                    var targetPath = match.Groups["TargetPath"].Value;
                    var validationPath = match.Groups["ValidationPath"].Value;

                    Debug.Assert(pageSpace == "root");
                    yield return (BuildPostbackPayload(vm.Value, commandId, controlId, validationPath, targetPath), commandId);
                }
            }
        }

        private static string BuildPostbackPayload(JObject vm, string commandId, string controlId, string validationPath, string targetPathArray)
        {
            var viewModel = vm["viewModel"].DeepClone();
            var targetPath = JArray.Parse(targetPathArray);
            // TODO: filter out unnecesary elements
            return new JObject(
                new JProperty("viewModel", viewModel),
                new JProperty("currentPath", targetPath),
                new JProperty("command", commandId),
                new JProperty("controlUniqueId", controlId),
                new JProperty("validationTargetPath", validationPath),
                new JProperty("renderedResources", vm["renderedResources"].DeepClone())
            ).ToString();
        }

        public static IEnumerable<Benchmark> CreatePostbackBenchmarks(Benchmark b, DotvvmTestHost host, DotvvmConfiguration config)
        {
            var urls = GetTestRoutes(config);
            var urlDefinition = new ParameterDefinition(nameof(DotvvmPostbackBenchmarks<TAppLauncher>.Url), false, new object[] { });
            var vmDefiniton = new ParameterDefinition(nameof(DotvvmPostbackBenchmarks<TAppLauncher>.SerializedViewModel), false, new object[] { });
            Directory.CreateDirectory("testViewModels");
            var result = new ConcurrentBag<Benchmark>();
            Parallel.ForEach(urls, url => {
                try
                {
                    var html = host.GetRequest(url).Result.Contents;
                    foreach (var (json, name) in FindPostbacks(html))
                    {
                        var fname = $"{ new string(name.Where(char.IsLetterOrDigit).ToArray()) }_{ json.GetHashCode() }";
                        File.WriteAllText($"testViewModels/{fname}.json", json);
                        try
                        {
                            new DotvvmPostbackBenchmarks<TAppLauncher> { Url = url, SerializedViewModel = fname }.TestDotvvmRequest();
                        }
                        catch { continue; }
                        result.Add(new Benchmark(b.Target, b.Job, new ParameterInstances(new[] {
                            new ParameterInstance(urlDefinition, url),
                            new ParameterInstance(vmDefiniton, fname)
                        })));
                    }
                }
                catch { }
            });
            return result;
        }
    }
    public class DotvvmGetBenchmarks<T>
             where T : IApplicationLauncher, new()

    {
        private DotvvmTestHost host = DotvvmSamplesBenchmarker<T>.CreateSamplesTestHost();

        public string Url { get; set; }

        [Benchmark]
        public void TestDotvvmRequest()
        {
            var r = host.GetRequest(Url).Result;
            if (string.IsNullOrEmpty(r.Contents)) throw new Exception("Result was empty");
        }
    }

    public class DotvvmPostbackBenchmarks<T>
            where T : IApplicationLauncher, new()

    {
        Dictionary<string, string> viewModels;
        public DotvvmPostbackBenchmarks()
        {
            viewModels = System.IO.Directory.EnumerateFiles("testViewModels", "*.json").ToDictionary(Path.GetFileNameWithoutExtension, File.ReadAllText);
        }

        private DotvvmTestHost host = DotvvmSamplesBenchmarker<T>.CreateSamplesTestHost();
        public string Url { get; set; }
        public string SerializedViewModel { get; set; }

        [Benchmark]
        public void TestDotvvmRequest()
        {
            var r = host.PostRequest(Url, viewModels[SerializedViewModel]).Result;
            if (string.IsNullOrEmpty(r.Contents)) throw new Exception("Result was empty");
        }
    }
}
