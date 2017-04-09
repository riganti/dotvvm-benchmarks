using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Parameters;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using DotVVM.Framework.Configuration;

namespace DotVVM.Benchmarks
{
    public class DotvvmSamplesBenchmarker<TDotvvmStartup>
            where TDotvvmStartup : IDotvvmStartup, new()
    {
        public static Summary BenchmarkSamples(IConfig config)
        {
            var host = CreateSamplesTestHost();
            var benchmarks = GetAllBenchmarks(config, host).ToArray();

            return BenchmarkRunner.Run(benchmarks, config);
        }

        public static DotvvmTestHost CreateSamplesTestHost()
        {
            var currentPath = Directory.GetCurrentDirectory();
            while (Path.GetFileName(currentPath) != "DotVVM.Benchmarks") currentPath = Path.GetDirectoryName(currentPath);
            currentPath = Path.Combine(Path.GetDirectoryName(currentPath), "dotvvm/src/DotVVM.Samples.Common");
            return DotvvmTestHost.Create<TDotvvmStartup>(currentPath);
        }

        private static IEnumerable<Benchmark> GetAllBenchmarks(IConfig config, DotvvmTestHost testHost)
        {
            return BenchmarkConverter.TypeToBenchmarks(typeof(DotvvmSamplesBenchmarks), config)
                .SelectMany(b => CreateBenchmarks(b, testHost, testHost.Configuration));
        }

        public static IEnumerable<Benchmark> CreateBenchmarks(Benchmark b, DotvvmTestHost host, DotvvmConfiguration config)
        {
            var requests = config.RouteTable.Select(r => new DotvvmSamplesBenchmarks.RequestInfo {
                RouteName = r.RouteName,
                RouteParams = r.ParameterNames.ToDictionary(i => i, e => r.DefaultValues.TryGetValue(e, out var obj) ? obj : "5"),
                TestHost = host
            })
            .Where(r => r.RouteParams.Count == 0)
            .Select(r => r.RouteName)
            .Where(r => !r.Contains("Auth") && !r.Contains("SPARedirect")); // Auth samples cause problems, because thei viewModels are not loaded
            var definiton = new ParameterDefinition(nameof(DotvvmSamplesBenchmarks.RouteInfo), false, new DotvvmSamplesBenchmarks.RequestInfo[0]);
            foreach (var value in requests)
            {
                yield return new Benchmark(b.Target, b.Job, new ParameterInstances(new[] { new ParameterInstance(definiton, value) }));
            }
        }

        public class DotvvmSamplesBenchmarks
        {
            public class RequestInfo
            {
                public string RouteName { get; set; }
                public Dictionary<string, object> RouteParams { get; set; }
                public DotvvmTestHost TestHost { get; set; }
            }

            private DotvvmTestHost host = CreateSamplesTestHost();

            public string RouteInfo { get; set; }

            [Benchmark]
            public void TestDotvvmRequest()
            {
                var url = "/" + host.Configuration.RouteTable[RouteInfo].BuildUrl().TrimStart('~', '/');
                var r = host.GetRequest(url).Result;
                if (string.IsNullOrEmpty(r.Contents)) throw new Exception("Result was empty");
            }
        }
    }
}
