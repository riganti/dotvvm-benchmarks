using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace DotVVM.Benchmarks.Benchmarks
{
    public class TestViewModel
    {
        public int Property { get; set; }
    }
    public class RequestBenchmarks
    {
        private DotvvmTestHost host;
        public RequestBenchmarks()
        {
            host = DotvvmTestHost.Create<DotvvmTestHost.DefaultLauncher>();
        }

        [Benchmark]
        public void TestParallelFor()
        {
            var ccc = host.AddFile("<html><head></head><body></body></html>", typeof(object));
            Parallel.For(0, 50, i => {
                var tt = host.GetRequest(ccc);
                tt.Wait();
            });
        }

        [Benchmark]
        public void TestMininalPage()
        {
            var ccc = host.AddFile("<html><head></head><body></body></html>", typeof(object));

            var tt = host.GetRequest(ccc);
            tt.Wait();
        }

        private static string Page1000Bindings =
            $@"
<html>
<head>
</head>
<body>
<div>
    {string.Concat(Enumerable.Repeat("{{value: Property}} ", 1000))}
</div>
</body>
</html>
";

        [Benchmark]
        public void Test1000Bindins()
        {
            var ccc = host.AddFile(Page1000Bindings, typeof(TestViewModel));
            var tt = host.GetRequest(ccc);
            tt.Wait();
        }
    }
}