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
        static HashSet<string> urlBlacklist = new HashSet<string> {
            // long sleeps & nothing interesting
            "DotvvmSamplesLauncher:FeatureSamples/BindingPageInfo/BindingPageInfo",
            "DotvvmSamplesLauncher:ControlSamples/UpdateProgress/UpdateProgress",
            "DotvvmSamplesLauncher:ControlSamples/UpdateProgress/UpdateProgressDelay",
            "DotvvmSamplesLauncher:FeatureSamples/ViewModelNesting/NestedViewModel",

            // almost empty redundant pages
            "DotvvmSamplesLauncher:ControlSamples/RouteLink/TestRoute",
            "DotvvmSamplesLauncher:ControlSamples/RouteLink/RouteLinkEnabledFalse",
            "DotvvmSamplesLauncher:ControlSamples/RouteLink/RouteLinkEnabled",
            "DotvvmSamplesLauncher:ControlSamples/Literal/Literal_ArrayLength",
            "DotvvmSamplesLauncher:ControlSamples/Literal/Literal_CollectionLength",
            "DotvvmSamplesLauncher:ControlSamples/ComboBox/ComboBoxTitle",
            "DotvvmSamplesLauncher:ControlSamples/ComboBox/ComboBoxDelaySync3",
            "DotvvmSamplesLauncher:ControlSamples/ComboBox/ComboBoxDelaySync2",
            "DotvvmSamplesLauncher:ControlSamples/ComboBox/ComboBoxDelaySync",
            "DotvvmSamplesLauncher:ControlSamples/Button/InputTypeButton_TextContentInside",
            "DotvvmSamplesLauncher:ControlSamples/Button/Button",
            "DotvvmSamplesLauncher:ControlSamples/Button/Button",
            "DotvvmSamplesLauncher:ComplexSamples/NamespaceCollision/NamespaceCollision",
            "DotvvmSamplesLauncher:FeatureSamples/Api/AzureFunctionsApi",
            "DotvvmSamplesLauncher:FeatureSamples/Api/AzureFunctionsApiTable",
            "DotvvmSamplesLauncher:FeatureSamples/Api/GetCollection",
            "DotvvmSamplesLauncher:FeatureSamples/Api/GridViewDataSetAspNetCore",
            "DotvvmSamplesLauncher:FeatureSamples/Api/GridViewDataSetOwin",
            "DotvvmSamplesLauncher:FeatureSamples/CommandArguments/CommandArguments",
            "DotvvmSamplesLauncher:FeatureSamples/CommandArguments/ReturnValue",
            "DotvvmSamplesLauncher:FeatureSamples/Directives/ImportDirectiveInvalid",
            "DotvvmSamplesLauncher:FeatureSamples/Directives/ImportDirective",
            "DotvvmSamplesLauncher:FeatureSamples/Directives/ViewModelMissingAssembly",
            "DotvvmSamplesLauncher:FeatureSamples/GenericTypes/InCommandBinding",
            "DotvvmSamplesLauncher:FeatureSamples/GenericTypes/InResourceBinding",
            "DotvvmSamplesLauncher:FeatureSamples/GenericTypes/InStaticCommandBinding",
            "DotvvmSamplesLauncher:FeatureSamples/HtmlTag/NonPairHtmlTag",
            "DotvvmSamplesLauncher:FeatureSamples/JavascriptEvents/JavascriptEvents",
            "DotvvmSamplesLauncher:FeatureSamples/Localization/Localization",
            "DotvvmSamplesLauncher:FeatureSamples/Localization/Localization_NestedPage_Type",
            "DotvvmSamplesLauncher:FeatureSamples/Localization/Localization_Control_Page",
            "DotvvmSamplesLauncher:FeatureSamples/NestedMasterPages/Content",
            "DotvvmSamplesLauncher:FeatureSamples/ServerComments/ServerComments",
            // testing just javascript functionality
            "DotvvmSamplesLauncher:FeatureSamples/Resources/CdnScriptPriority",
            "DotvvmSamplesLauncher:FeatureSamples/Resources/CdnUnavailableResourceLoad",
            "DotvvmSamplesLauncher:FeatureSamples/Resources/OnlineNonameResourceLoad",
            "DotvvmSamplesLauncher:FeatureSamples/Serialization/ObservableCollectionShouldContainObservables",

            "DotvvmSamplesLauncher:FeatureSamples/Validation/ClientSideValidationDisabling",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/CustomValidation",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/DateTimeValidation_NullableDateTime",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/DateTimeValidation",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/DynamicValidation",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/EssentialTypeValidation",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/Localization",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/ModelStateErrors",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/NestedValidation",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/NullValidationTarget",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/RegexValidation",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/ValidationRulesLoadOnPostback",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/ValidationScopes",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/ValidationScopes2",
            "DotvvmSamplesLauncher:FeatureSamples/Validation/NullValidationTarget",

            "DotvvmSamplesLauncher:FeatureSamples/ViewModelDeserialization/DoesNotDropObject",
            "DotvvmSamplesLauncher:FeatureSamples/ViewModelDeserialization/NegativeLongNumber",
        };
        public static IEnumerable<BenchmarkRunInfo> BenchmarkSamples(IConfig config, bool getRequests = true, bool postRequests = true)
        {
            var host = CreateSamplesTestHost();
            var getBenchmarks = getRequests ? new [] { AllGetBenchmarks(config, host) } : new BenchmarkRunInfo[0];
            var postBenchmarks = postRequests ? new [] { AllPostBenchmarks(config, host) } : new BenchmarkRunInfo[0];

            return getBenchmarks.Concat(postBenchmarks).ToArray();
        }

        public static IEnumerable<BenchmarkRunInfo> BenchmarkMvcSamples(IConfig config, bool getRequests = true, bool postRequests = true)
        {
            var host = CreateSamplesTestHost();
            var bb = AllMvcBenchmarks(config, host);
            yield return bb;
        }

        public static string GetRootPath()
        {
            var currentPath = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(currentPath, "DotVVM.Benchmarks.sln"))) currentPath = Path.GetDirectoryName(currentPath);
            return currentPath;
        }

        public static DotvvmTestHost CreateSamplesTestHost()
        {
            return DotvvmTestHost.Create<TAppLauncher>(GetRootPath());
        }

        public static BenchmarkRunInfo AllMvcBenchmarks(IConfig config, DotvvmTestHost testHost)
        {
            IEnumerable<BenchmarkCase> createMvcBenchmarks(BenchmarkCase b)
            {
                var urls = new[] { "/Home/Index" };
                var definiton = new ParameterDefinition(nameof(DotvvmGetBenchmarks<TAppLauncher>.Url), false, new object[] { }, false);
                foreach (var url in urls)
                {
                    new DotvvmGetBenchmarks<TAppLauncher> { Url = url }.Get();
                    yield return BenchmarkCase.Create(b.Descriptor, b.Job, new ParameterInstances(new[] { new ParameterInstance(definiton, url) }));
                }
            }
            var runInfo = BenchmarkConverter.TypeToBenchmarks(typeof(DotvvmGetBenchmarks<TAppLauncher>), config);
            var cases = runInfo.BenchmarksCases.SelectMany(createMvcBenchmarks).ToArray();
            return new BenchmarkRunInfo(cases, runInfo.Type, runInfo.Config);
        }

        private static BenchmarkRunInfo AllGetBenchmarks(IConfig config, DotvvmTestHost testHost)
        {
            var runInfo = BenchmarkConverter.TypeToBenchmarks(typeof(DotvvmGetBenchmarks<TAppLauncher>), config);
            var cases = runInfo.BenchmarksCases
                .SelectMany(b => CreateBenchmarks(b, testHost, testHost.Configuration)).ToArray();
            return new BenchmarkRunInfo(cases, runInfo.Type, runInfo.Config);
        }

        private static BenchmarkRunInfo AllPostBenchmarks(IConfig config, DotvvmTestHost testHost)
        {
            Directory.CreateDirectory("testViewModels");
            var runInfo = BenchmarkConverter.TypeToBenchmarks(typeof(DotvvmPostbackBenchmarks<TAppLauncher>), config);
            var cases = runInfo.BenchmarksCases
                .SelectMany(b => CreatePostbackBenchmarks(b, testHost, testHost.Configuration))
                .ToArray();
            return new BenchmarkRunInfo(cases, runInfo.Type, runInfo.Config);
        }

        public static IEnumerable<string> GetTestRoutes(DotvvmConfiguration config) =>
            config.RouteTable
            // TODO support parameters somehow
            .Where(r => r.ParameterNames.Count() == 0)
            .Where(r => !r.RouteName.Contains("Auth") && !r.RouteName.Contains("SPARedirect") && !r.RouteName.Contains("Error")) // Auth samples cause problems, because thei viewModels are not loaded
            .Select(r => r.BuildUrl().TrimStart('~'))
            .Where(r => !urlBlacklist.Contains(typeof(TAppLauncher).Name + ":" + r.TrimStart('/')));

        public static IEnumerable<BenchmarkCase> CreateBenchmarks(BenchmarkCase b, DotvvmTestHost host, DotvvmConfiguration config)
        {
            var urls = GetTestRoutes(config);
            var definiton = new ParameterDefinition(nameof(DotvvmGetBenchmarks<TAppLauncher>.Url), false, new object[] { }, false);
            foreach (var url in urls)
            {
                try
                {
                    new DotvvmGetBenchmarks<TAppLauncher>(host) { Url = url }.Get();
                }
                catch { continue; }
                yield return BenchmarkCase.Create(b.Descriptor, b.Job, new ParameterInstances(new[] { new ParameterInstance(definiton, url) }));
            }
        }

        // V2.0 sample: <dotvvm.postBack("root",this,["Children/[$index]", "Children/[$index]"],"J0P/GrlKdT/ylW4Q","",null,["validate-root"],undefined);event.stopPropagation();return false;>
        private static Regex postbackScriptRegex1 = new Regex(@".*dotvvm\.postback(script\(['""](?<CommandId>.+)['""]\))?[^a-zA-Z0-9'""]*['""](?<PageSpace>.*)['""],[^a-zA-Z0-9'""]*this,[^a-zA-Z0-9'""]*(?<TargetPath>\[.*\]),[^a-zA-Z0-9'""]*(['""](?<CommandId>.+)['""],[^a-zA-Z0-9'""]*)?['""](?<ControlId>.*)['""][^][a-zA-Z0-9'""]*(true|false|null)[^][a-zA-Z0-9'""]*(['""](?<ValidationPath>.+)['""],)?.*?(['""](?<ValidationPH>validate.*)['""])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static Regex postbackScriptRegex2 = new Regex(@".*dotvvm\.postback(script\(['""](?<CommandId>.+)['""]\))?[^a-zA-Z0-9'""]*['""](?<PageSpace>.*)['""],[^a-zA-Z0-9'""]*this,[^a-zA-Z0-9'""]*(?<TargetPath>\[.*\]),[^a-zA-Z0-9'""]*(['""](?<CommandId>.+)['""],[^a-zA-Z0-9'""]*)?['""](?<ControlId>.*)['""][^][a-zA-Z0-9'""]*(true|false|null)[^][a-zA-Z0-9'""]*(['""](?<ValidationPath>.+)['""],)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
                    var match = postbackScriptRegex1.Match(attribute.Value);
                    if (!match.Success) match = postbackScriptRegex2.Match(attribute.Value);
                    if (!match.Success) continue;
                    var commandId = match.Groups["CommandId"].Value;
                    var controlId = match.Groups["ControlId"].Value;
                    var pageSpace = match.Groups["PageSpace"].Value;
                    var targetPath = match.Groups["TargetPath"].Value;
                    var validationPath = match.Groups["ValidationPath"].Value;


                    targetPath = targetPath.Replace("[$index]", "[0]");

                    if (String.IsNullOrEmpty(validationPath))
                    {
                        var valHandler = match.Groups["ValidationPH"].Value;
                        if (valHandler == "validate-root")
                            validationPath = "dotvvm.viewModelObservables['root']";
                        else if (valHandler == "validate-this")
                            validationPath = "$data";
                        else if (String.IsNullOrEmpty(valHandler))
                            validationPath = null;
                        else if (valHandler.StartsWith("validate\", {path:\""))
                            validationPath = valHandler.Substring("validate\", {path:\"".Length);
                        else
                            Console.WriteLine($"unsupported validation handler = `{valHandler}`");
                    }

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
                new JProperty("additionalData",
                    new JObject(
                        new JProperty("validationTargetPath", validationPath))),
                new JProperty("renderedResources", vm["renderedResources"].DeepClone())
            ).ToString();
        }

        public static IEnumerable<BenchmarkCase> CreatePostbackBenchmarks(BenchmarkCase b, DotvvmTestHost host, DotvvmConfiguration config)
        {
            var urls = GetTestRoutes(config);
            var urlDefinition = new ParameterDefinition(nameof(DotvvmPostbackBenchmarks<TAppLauncher>.Url), false, new object[] { }, false);
            var vmDefiniton = new ParameterDefinition(nameof(DotvvmPostbackBenchmarks<TAppLauncher>.SerializedViewModel), false, new object[] { }, false);
            var viewModelDirectory = Environment.GetEnvironmentVariable("DotvvmTests_ViewModelDirectory") ??
                Path.GetFullPath("testViewModels");
            Environment.SetEnvironmentVariable("DotvvmTests_ViewModelDirectory", viewModelDirectory);
            Directory.CreateDirectory(viewModelDirectory);
            var result = new ConcurrentBag<BenchmarkCase>();
            Parallel.ForEach(urls, url =>
            {
                try
                {
                    var html = host.GetRequest(url).Result.Contents;
                    foreach (var (json, name) in FindPostbacks(html))
                    {
                        var fname = $"{ new string(url.Where(char.IsLetterOrDigit).ToArray()) }";
                        File.WriteAllText(Path.Combine(viewModelDirectory, $"{fname}.json"), json);
                        try
                        {
                            new DotvvmPostbackBenchmarks<TAppLauncher>(host) { Url = url, SerializedViewModel = fname }.Postback();
                        }
                        catch { continue; }
                        result.Add(BenchmarkCase.Create(b.Descriptor, b.Job, new ParameterInstances(new[] {
                            new ParameterInstance(urlDefinition, url),
                            new ParameterInstance(vmDefiniton, fname)
                        })));

                        // let's take only the first working post request on a page
                        break;
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
        public DotvvmGetBenchmarks() : this(DotvvmSamplesBenchmarker<T>.CreateSamplesTestHost())
        {
        }
        public DotvvmGetBenchmarks(DotvvmTestHost host)
        {
            this.host = host;
        }
        private DotvvmTestHost host;

        public string Url { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            Get();
        }

        [Benchmark]
        public void Get()
        {
            try
            {
                var r = SynchronousMemoryDiagnoser.RunTask(() => host.GetRequest(Url));
                if (string.IsNullOrEmpty(r.Contents)) throw new Exception("Result was empty");
            }
            catch { }
        }
    }

    public class DotvvmPostbackBenchmarks<T>
            where T : IApplicationLauncher, new()

    {
        Dictionary<string, Lazy<string>> viewModels;
        public DotvvmPostbackBenchmarks() : this(DotvvmSamplesBenchmarker<T>.CreateSamplesTestHost()) { }
        public DotvvmPostbackBenchmarks(DotvvmTestHost host)
        {
            viewModels = System.IO.Directory.EnumerateFiles(Environment.GetEnvironmentVariable("DotvvmTests_ViewModelDirectory"), "*.json").ToDictionary(Path.GetFileNameWithoutExtension, f => new Lazy<string>(() => File.ReadAllText(f)));
            this.host = host;
        }
        private DotvvmTestHost host;
        public string Url { get; set; }
        public string SerializedViewModel { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            Postback();
        }

        [Benchmark]
        public void Postback()
        {
            try
            {
                var r = SynchronousMemoryDiagnoser.RunTask(() => host.PostRequest(Url, viewModels[SerializedViewModel].Value));
                if (string.IsNullOrEmpty(r.Contents)) throw new Exception("Result was empty");
            }
            catch { }
        }
    }
}
