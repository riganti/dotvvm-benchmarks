using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotVVM.Framework.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Razor;
using System.IO;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.CodeAnalysis;
using System.Reflection;

namespace DotVVM.Benchmarks.MvcWebApp
{
    public class MvcAppLauncher : IApplicationLauncher
    {
        public DotvvmConfiguration ConfigApp(IApplicationBuilder app, string currentPath)
        {
            app.UseMvc(routes => {
                routes.MapRoute("default",
                    template: "{controller}/{action}/{id?}",
                    defaults: new { controller = "Home", action = "Index" }
                );
            });
            return null;
        }

        public void ConfigServices(IServiceCollection services, string currentPath)
        {
            services.Configure<RazorViewEngineOptions>(o => {
                o.FileProviders.Add(new FileProvider());
                o.ViewLocationExpanders.Add(new MyLocationExpander(currentPath));
            });
            services.AddMvc(o => {
            }).AddRazorOptions(options => {
                var previous = options.CompilationCallback;
                options.CompilationCallback = context => {
                    HashSet<string> allTheReferences(Assembly a, HashSet<string> h = null)
                    {
                        h = h ?? new HashSet<string>();
                        foreach (var rf in a.GetReferencedAssemblies())
                        {
                            if (rf.FullName.Contains("PerfView")) continue;
                            try
                            {
                                Assembly assembly = Assembly.Load(rf.FullName);
                                if (h.Add(assembly.Location))
                                {
                                    allTheReferences(assembly, h);
                                }
                            }
                            catch { }
                        }
                        return h;
                    }
                    previous?.Invoke(context);
                    context.Compilation = context.Compilation.AddReferences(MetadataReference.CreateFromFile(typeof(MyLocationExpander).Assembly.Location)).AddReferences(allTheReferences(typeof(MyLocationExpander).Assembly).Select(a => MetadataReference.CreateFromFile(a)));
                };
            });
        }

        class MyLocationExpander : IViewLocationExpander
        {
            private readonly string currentPath;

            public MyLocationExpander(string currentPath)
            {
                this.currentPath = Path.Combine(currentPath, "DotVVM.Benchmarks/MvcWebApp");
            }

            public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
            {
                return viewLocations.Select(l => Path.Combine(currentPath, l.TrimStart('/')));
            }

            public void PopulateValues(ViewLocationExpanderContext context)
            {
                context.Values.Add("Index", Path.Combine(currentPath, context.ControllerName, context.ViewName + ".cshtml"));
            }
        }

        private class FileProvider : IFileProvider
        {
            public IDirectoryContents GetDirectoryContents(string subpath)
            {
                throw new NotImplementedException();
            }

            public IFileInfo GetFileInfo(string subpath)
            {
                return new PhysicalFileInfo(new FileInfo(subpath.TrimStart('/')));
                //throw new NotImplementedException();
            }

            public IChangeToken Watch(string filter)
            {
                return new NopChnageToken();
            }
            class NopChnageToken : IChangeToken
            {
                public bool HasChanged => false;

                public bool ActiveChangeCallbacks => false;

                public IDisposable RegisterChangeCallback(Action<object> callback, object state)
                {
                    return new MemoryStream();
                }
            }
        }
    }
}
