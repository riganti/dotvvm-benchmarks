using System;
using System.Linq;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using DotVVM.Framework.Compilation.Parser.Dothtml.Tokenizer;
using DotVVM.Framework.Compilation.Parser.Dothtml.Parser;

namespace DotVVM.Benchmarks.Benchmarks
{
    public class ParserBenchmarks
    {
        public static DothtmlRootNode TokenizeAndParse(string dothtml)
        {
            var t = new DothtmlTokenizer();
            t.Tokenize(dothtml);
            var p = new DothtmlParser();
            return p.Parse(t.Tokens);
        }

        public static List<DothtmlToken> Tokenize(string dothtml)
        {
            var t = new DothtmlTokenizer();
            t.Tokenize(dothtml);
            return t.Tokens;
        }

        [Benchmark]
        public object Tokenize()
        {
            return Tokenize(TestPages[PageName]);
        }

        [Benchmark]
        public object TokenizeAndParse()
        {
            return TokenizeAndParse(TestPages[PageName]);
        }

        [BenchmarkDotNet.Attributes.Params("SimpleHtmlPage", "DothtmlPage1")]
        public string PageName;

        private static string LoadResource(string name)
        {
            using (var reader = new System.IO.StreamReader(typeof(ParserBenchmarks).Assembly.GetManifestResourceStream(name)))
                return reader.ReadToEnd();
        }

        private static Dictionary<string, string> TestPages =
            new [] { "SimpleHtmlPage", "DothtmlPage1" }
            .ToDictionary(key => key, key => LoadResource($"DotVVM.Benchmarks.Resources.{key}"));
    }
}