using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotVVM.Framework.Configuration;
using DotVVM.Framework.Hosting;
using DotVVM.Framework.Security;
using DotVVM.Framework.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DotVVM.Benchmarks
{
    class FakeCsrfProtector : ICsrfProtector
    {
        public string GenerateToken(IDotvvmRequestContext context)
        {
            return "CSRF TOKEN";
        }

        public void VerifyToken(IDotvvmRequestContext context, string token)
        {
        }
    }

    public class DotvvmTestHost
    {
        public readonly TestServer Server;
        public readonly DotvvmConfiguration Configuration;
        public readonly HttpClient Client;

        private int fileCounter = 1;

        public DotvvmTestHost(TestServer server, DotvvmConfiguration configuration, HttpClient client)
        {
            this.Server = server;
            this.Configuration = configuration;
            this.Client = client;
        }


        public static DotvvmTestHost Create<TDotvvmStartup>(string applicationPath = "`undefinedLocation")
            where TDotvvmStartup: IDotvvmStartup, new()
        {
            DotvvmConfiguration configuration = null;
            var builder = new WebHostBuilder()
                .ConfigureServices(s => {
                    s.AddSingleton<IMarkupFileLoader>(_ => new VirtualMarkupFileLoader(new DefaultMarkupFileLoader()));
                    s.AddSingleton<ICsrfProtector, FakeCsrfProtector>();
                    s.AddDotVVM();
                })
                .Configure(a => {
                    configuration = a.UseDotVVM<TDotvvmStartup>(applicationPath, useErrorPages: false);
                });
            var testServer = new TestServer(builder);
            var testClient = testServer.CreateClient();

            return new DotvvmTestHost(testServer, configuration, testClient);
        }

        public async Task<DotvvmGetResponse> GetRequest(string url)
        {
            var response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            return new DotvvmGetResponse(response, await response.Content.ReadAsStringAsync());
        }

        public async Task<DotvvmGetResponse> PostRequest(string url, string payload, string type = "text/json")
        {
            var response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, type), Headers = { { HostingConstants.SpaPostBackHeaderName, "true" } } });
            return new DotvvmGetResponse(response, await response.Content.ReadAsStringAsync());
        }

        public void AddFile(string route, string contents)
        {
            var fileName = $"file{Interlocked.Increment(ref fileCounter)}.dothtml";
            Configuration.RouteTable.Add(fileName, route, fileName);
            Configuration.ServiceLocator.GetService<IMarkupFileLoader>().CastTo<VirtualMarkupFileLoader>().PathToFileMap
                .TryAdd(fileName, contents);
        }

        private ConcurrentDictionary<string, string> fileCache = new ConcurrentDictionary<string, string>();
        public string AddFile(string dothtml)
        {
            return fileCache.GetOrAdd(dothtml, d => {
                var name = $"route{Interlocked.Increment(ref fileCounter)}";
                AddFile(name, d);
                return name;
            });
        }

        public string AddFile(string dothtml, Type viewModel)
        {
            return AddFile($"@viewModel {viewModel.FullName}\n" + dothtml);
        }

        public class VirtualMarkupFileLoader: IMarkupFileLoader
        {
            public ConcurrentDictionary<string, string> PathToFileMap = new ConcurrentDictionary<string, string>();
            private readonly IMarkupFileLoader fallback;

            public VirtualMarkupFileLoader(IMarkupFileLoader fallback)
            {
                this.fallback = fallback;
            }

            public MarkupFile GetMarkup(DotvvmConfiguration configuration, string virtualPath)
            {
                var mf = new MarkupFile(virtualPath, virtualPath);
                if (PathToFileMap.TryGetValue(virtualPath, out var contents))
                {
                    mf.GetType().GetProperty(nameof(MarkupFile.ContentsReaderFactory)).SetValue(mf, (Func<string>)(() => contents));
                    return mf;
                }
                else
                {
                    return fallback.GetMarkup(configuration, virtualPath);
                }
            }

            public string GetMarkupFileVirtualPath(IDotvvmRequestContext context)
            {
                return context.Route.VirtualPath;
            }
        }

        public class DotvvmStartup : IDotvvmStartup
        {
            public void Configure(DotvvmConfiguration config, string applicationPath)
            {
                
            }
        }
    }

    public class DotvvmGetResponse
    {
        public HttpResponseMessage HttpResponse { get; }
        public string Contents { get; }

        public DotvvmGetResponse(HttpResponseMessage httpResponse, string contents)
        {
            this.HttpResponse = httpResponse;
            this.Contents = contents;
        }
    }
}
