﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
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

        private static int undefinedLocationCounter = 0;

        public static (IWebHostBuilder, StrongBox<DotvvmConfiguration>) InitializeBuilder<TAppLauncher>(string currentPath = null)
            where TAppLauncher : IApplicationLauncher, new()
        {
            currentPath = currentPath ?? ("`undefinedLocation" + Interlocked.Increment(ref undefinedLocationCounter));

            var configuration = new StrongBox<DotvvmConfiguration>(null);
            // DotvvmConfiguration configuration = null;
            var launcher = new TAppLauncher();
            var builder = new WebHostBuilder()
                .ConfigureServices(s => {
                    s.AddSingleton<IMarkupFileLoader>(_ => new VirtualMarkupFileLoader(new DefaultMarkupFileLoader()));
                    s.AddSingleton<ICsrfProtector, FakeCsrfProtector>();
                    launcher.ConfigServices(s, currentPath);
                })
                .Configure(a => {
                    configuration.Value = launcher.ConfigApp(a, currentPath); //.UseDotVVM<TDotvvmStartup>(applicationPath, useErrorPages: false);
                });
            return (builder, configuration);
        }

        public static DotvvmTestHost Create<TAppLauncher>(string currentPath = null)
            where TAppLauncher : IApplicationLauncher, new()
        {
            var (builder, configuration) = InitializeBuilder<TAppLauncher>(currentPath);
            var testServer = new TestServer(builder);
            var testClient = testServer.CreateClient();

            return new DotvvmTestHost(testServer, configuration.Value, testClient);
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
            Configuration.ServiceProvider.GetService<IMarkupFileLoader>().CastTo<VirtualMarkupFileLoader>().AddFile(fileName, contents);
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
            public ConcurrentDictionary<string, MarkupFile> PathToFileMap = new ConcurrentDictionary<string, MarkupFile>();
            private readonly IMarkupFileLoader fallback;

            public VirtualMarkupFileLoader(IMarkupFileLoader fallback)
            {
                this.fallback = fallback;
            }

            public void AddFile(string path, string contents)
            {
                var mf = new MarkupFile(path, path);
                (mf.GetType().GetProperty("ReadContent") ?? mf.GetType().GetProperty("ContentsReaderFactory")).SetValue(mf, (Func<string>)(() => contents));
                PathToFileMap[path] = mf;
            }

            public MarkupFile GetMarkup(DotvvmConfiguration configuration, string virtualPath)
            {
                return PathToFileMap.GetOrAdd(virtualPath, _ => fallback.GetMarkup(configuration, virtualPath));
            }

            public string GetMarkupFileVirtualPath(IDotvvmRequestContext context)
            {
                return context.Route.VirtualPath;
            }
        }

        public class DefaultLauncher : IApplicationLauncher
        {
            public DotvvmConfiguration ConfigApp(IApplicationBuilder app, string currentPath)
            {
                return app.UseDotVVM<DotvvmStartup>("`skks");
            }

            public void ConfigServices(IServiceCollection services, string currentPath)
            {
                services.AddDotVVM();
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

    public interface IApplicationLauncher
    {
        void ConfigServices(IServiceCollection services, string currentPath);
        DotvvmConfiguration ConfigApp(IApplicationBuilder app, string currentPath);
    }
}
