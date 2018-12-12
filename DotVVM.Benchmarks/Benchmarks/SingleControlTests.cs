using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using DotVVM.Framework.Binding;
using DotVVM.Framework.Binding.Expressions;
using DotVVM.Framework.Compilation.ControlTree;
using DotVVM.Framework.Configuration;
using DotVVM.Framework.Controls;
using DotVVM.Framework.Controls.Infrastructure;
using DotVVM.Framework.Hosting;
using DotVVM.Framework.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DotVVM.Benchmarks.Benchmarks
{
    public class SingleControlTests
    {
        private readonly DotvvmConfiguration configuration = DotvvmSamplesBenchmarker<DotvvmSamplesLauncher>.CreateSamplesTestHost().Configuration;
        private readonly IDotvvmRequestContext context;
        private readonly StringBuilder output = new StringBuilder();
        private readonly HtmlWriter writer;
        private readonly DotvvmView rootView = new DotvvmView();
        private readonly TestViewModel viewModel = new TestViewModel() { Property = 135 };

        private readonly IValueBinding testValueBinding;
        public SingleControlTests()
        {
            context = new TestDotvvmRequestContext() {
                Configuration = configuration
            };
            writer = new HtmlWriter(new StringWriter(output), context);
            Internal.MarkupFileNameProperty.SetValue(rootView, "some_fake_path");
            Internal.RequestContextProperty.SetValue(rootView, "some_fake_path");

            var bcs = context.Services.GetService<BindingCompilationService>();
            var dataContext = DataContextStack.Create(typeof(TestViewModel));

            Internal.DataContextTypeProperty.SetValue(rootView, dataContext);
            DotvvmBindableObject.DataContextProperty.SetValue(rootView, viewModel);

            testValueBinding = ValueBindingExpression.CreateBinding(bcs, h => ((TestViewModel)h[0]).Property, dataContext);
        }
        private void InitAndRender(Func<DotvvmControl> create)
        {
            output.Clear();
            var c = create();
            Internal.MarkupLineNumberProperty.SetValue(c, 12);
            Internal.UniqueIDProperty.SetValue(c, "c_23");
            rootView.Children.Add(c);
            c.Render(writer, context);
            rootView.Children.Clear();
        }

        [Benchmark]
        public void RenderBoundLiteral() => InitAndRender(() => {
            var l = new Literal(allowImplicitLifecycleRequirements: false);
            Literal.TextProperty.SetValue(l, testValueBinding);
            return l;
        });

        [Benchmark]
        public void RenderEmptyLiteral() => InitAndRender(() => {
            var l = new Literal(allowImplicitLifecycleRequirements: false);
            Literal.TextProperty.SetValue(l, "");
            return l;
        });
    }
}