using System;
using System.Linq;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using DotVVM.Framework.Compilation.Parser.Dothtml.Tokenizer;
using DotVVM.Framework.Compilation.Parser.Dothtml.Parser;
using DotVVM.Framework.Controls;
using System.IO;
using DotVVM.Framework.Testing;
using Newtonsoft.Json;
using System.Text;

namespace DotVVM.Benchmarks.Benchmarks
{
    public class HtmlWriterBenchmarks
    {
        StringBuilder text;
        HtmlWriter writer;

		public HtmlWriterBenchmarks()
		{
            text = new StringBuilder();
			this.writer = new HtmlWriter(new StringWriter(text), DotvvmTestHelper.CreateContext());
		}

        static string longJsonString = JsonConvert.SerializeObject(Enumerable.Range(0, 100).Select(i => new { a = i, b = i, c = i, d = i, e = i, f = i, g = i, h = i, i = i, j = i, k = i, l = i, m = i, n = i, o = i, p = i, q = i, r = i, s = i, t = i, u = i, v = i, w = i, x = i, y = i.ToString(), z = i.ToString() }));

		[Benchmark]
        public void LongJsonString()
        {
            text.Clear();
            writer.AddAttribute("value", longJsonString);
            writer.RenderSelfClosingTag("bazmek");
        }

        static string shortHtmlString = """
            <tr class="spacer" style="height:5px"></tr>
                <tr class='athing' id='34414527'>
            <td align="right" valign="top" class="title"><span class="rank">22.</span></td>      <td valign="top" class="votelinks"><center><a id='up_34414527' class='clicky' href='vote?id=34414527&amp;how=up&amp;auth=d14b25526be3ce976e5da3dc5673fe99b1bb29c9&amp;goto=news'><div class='votearrow' title='upvote'></div></a></center></td><td class="title"><span class="titleline"><a href="item?id=34414527">Ask HN: Has anyone worked at the US National Labs before?</a></span></td></tr><tr><td colspan="2"></td><td class="subtext"><span class="subline">
            <span class="score" id="score_34414527">96 points</span> by <a href="user?id=science4sail" class="hnuser">science4sail</a> <span class="age" title="2023-01-17T16:27:52"><a href="item?id=34414527">3 hours ago</a></span> <span id="unv_34414527"></span> | <a href="flag?id=34414527&amp;auth=d14b25526be3ce976e5da3dc5673fe99b1bb29c9&amp;goto=news">flag</a> | <a href="hide?id=34414527&amp;auth=d14b25526be3ce976e5da3dc5673fe99b1bb29c9&amp;goto=news" class="clicky">hide</a> | <a href="item?id=34414527">93&nbsp;comments</a>        </span>
              </td></tr>
        """;

        static string longHtmlString = string.Concat(Enumerable.Repeat(shortHtmlString, longJsonString.Length / shortHtmlString.Length));

		[Benchmark]
        public void LongHtmlString()
        {
            text.Clear();
            writer.AddAttribute("value", longHtmlString);
            writer.RenderSelfClosingTag("bazmek");
        }
    }
}
