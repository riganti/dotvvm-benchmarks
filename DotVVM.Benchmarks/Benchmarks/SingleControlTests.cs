using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using DotVVM.Framework.Binding;
using DotVVM.Framework.Binding.Expressions;
using DotVVM.Framework.Binding.Properties;
using DotVVM.Framework.Compilation.ControlTree;
using DotVVM.Framework.Configuration;
using DotVVM.Framework.Controls;
using DotVVM.Framework.Controls.Infrastructure;
using DotVVM.Framework.Hosting;
using DotVVM.Framework.Testing;
using DotVVM.Framework.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace DotVVM.Benchmarks.Benchmarks
{
    // [DisassemblyDiagnoser(maxDepth: 2, printSource: true, exportHtml: true)] // Warning: often causes segfaults
    public class SingleControlTests
    {
        private readonly DotvvmConfiguration configuration = DotvvmSamplesBenchmarker<DotvvmSamplesLauncher>.CreateSamplesTestHost().Configuration;
        private readonly IDotvvmRequestContext context;
        private readonly StringBuilder output = new StringBuilder();
        private readonly HtmlWriter writer;
        private readonly DotvvmView rootView = new DotvvmView();
        private readonly TestViewModel viewModel = new TestViewModel() { Property = 135 };
        private readonly Random random = new Random();

        private readonly IValueBinding testValueBinding;
        private readonly IValueBinding<bool> boolValueBinding;
        private readonly IValueBinding<string[]> collectionValueBinding;
        private readonly IValueBinding<string> innerCollectionValueBinding;
        readonly HtmlGenericControl basicHtmlElement;
        readonly HtmlGenericControl richHtmlElement;
        readonly TextBox textBox = new TextBox { Text = "Abc" };

        readonly CloneTemplate[] prototypeCompositeControls;
        public SingleControlTests()
        {
            context = new TestDotvvmRequestContext() {
                Configuration = configuration,
                ResourceManager = new Framework.ResourceManagement.ResourceManager(configuration.Resources),
            };
            writer = new HtmlWriter(new StringWriter(output), context);
            Internal.MarkupFileNameProperty.SetValue(rootView, "some_fake_path");
            Internal.RequestContextProperty.SetValue(rootView, context);

            _ = context.Services.GetRequiredService<IControlResolver>(); // init Dotvvm properties
            var bcs = context.Services.GetService<BindingCompilationService>();
            var dataContext = DataContextStack.Create(typeof(TestViewModel));

            Internal.DataContextTypeProperty.SetValue(rootView, dataContext);
            DotvvmBindableObject.DataContextProperty.SetValue(rootView, viewModel);

            testValueBinding = ValueBindingExpression.CreateBinding(bcs, h => ((TestViewModel)h[0]).Property, dataContext);
            boolValueBinding = ValueBindingExpression.CreateBinding(bcs, h => ((TestViewModel)h[0]).Property == 0, dataContext);
            collectionValueBinding = ValueBindingExpression.CreateBinding(bcs, h => ((TestViewModel)h[0]).Collection, dataContext);
            innerCollectionValueBinding = ValueBindingExpression.CreateThisBinding<string>(bcs, collectionValueBinding.GetProperty<CollectionElementDataContextBindingProperty>().DataContext);

            basicHtmlElement = new HtmlGenericControl("div");
            richHtmlElement = new HtmlGenericControl("div");
            HtmlGenericControl.IncludeInPageProperty.SetValue(richHtmlElement, boolValueBinding);
            HtmlGenericControl.CssClassesGroupDescriptor.GetDotvvmProperty("my-class").SetValue(richHtmlElement, boolValueBinding);
            HtmlGenericControl.CssStylesGroupDescriptor.GetDotvvmProperty("width").SetValue(richHtmlElement, testValueBinding);
            richHtmlElement.Attributes.Add("data-my-attr", "HELLO");
            richHtmlElement.Attributes.Add("title", new(testValueBinding));

            Internal.UniqueIDProperty.SetValue(basicHtmlElement, "c1");
            Internal.UniqueIDProperty.SetValue(richHtmlElement, "c1");

            prototypeCompositeControls = Enumerable.Range(0, 16).Select(i => {
                var control = new TestCompositeControl();
                var cap = new TestCapability {
                    Text = i % 4 == 2 ? new TextOrContentCapability { Content = [new HtmlGenericControl("div").AddCssClass("a").AppendChildren(new Literal("Hello1"))] }
                                      : new TextOrContentCapability { Text = new("Hello2") },
                    BindableIntProperty = i % 3 != 1 ? new(5) : null,
                    InnerHtml = new HtmlCapability { },
                    Html = new HtmlCapability { },
                    BooleanProperty = i % 2 == 0
                };


                if (i % 4 == 3)
                {
                    cap.InnerHtml.Attributes.Add("class", new("some-class"));
                }

                if (i % 3 == 2)
                {
                    cap.Html.Attributes.Add("style", new("color: red"));
                }

                if (i % 2 == 1)
                {
                    cap.Html.CssClasses.Add("my-class", new(bcs.Cache.CreateValueBinding<bool>("Property < 45", dataContext)));
                }

                control.SetCapability(cap);

                // Console.WriteLine($"Control{i} has {control.Properties.Count} properties: {control.DebugString()}");
                return new CloneTemplate(control);
            }).ToArray();
        }
        private void InitAndRender(Func<DotvvmControl> create)
        {
            output.Clear();
            var c = create();
            Internal.MarkupLineNumberProperty.SetValue(c, 12);
            Internal.UniqueIDProperty.SetValue(c, "c_23");
            rootView.Children.Add(c);
            DotvvmControlCollection.InvokePageLifeCycleEventRecursive(c, LifeCycleEventType.PreRenderComplete, context);
            c.Render(writer, context);
            rootView.Children.Clear();
        }

        [Benchmark]
        public void RenderBoundLiteral() => InitAndRender(() => {
            var l = new Literal(testValueBinding);
            return l;
        });

        [Benchmark]
        public void RenderEmptyLiteral() => InitAndRender(() => {
            var l = new Literal("");
            return l;
        });

        [Benchmark]
        public void RenderTextBox() => InitAndRender(() => {
            var t = new TextBox();
            TextBox.TextProperty.SetValue(t, testValueBinding);
            t.Attributes.Add("class", "my-class");
            return t;
        });

        [Benchmark]
        public void RenderBasicHtmlElement() => InitAndRender(() => basicHtmlElement);
        [Benchmark]
        public void RenderRichHtmlElement() => InitAndRender(() => richHtmlElement);

        [Benchmark]
        public void SetProperty1()
        {
            this.textBox.Visible = !this.textBox.Visible;
        }

        [Benchmark]
        public void SetProperty2()
        {
            TextBox.VisibleProperty.SetValue(this.textBox, TextBox.VisibleProperty.GetValue(this.textBox, inherit: false));
            TextBox.VisibleProperty.SetValue(this.textBox, null);
            this.textBox.SetValue(TextBox.VisibleProperty, false);
            HtmlGenericControl.VisibleProperty.SetValue(this.textBox, BoxingUtils.False);
        }

        [Benchmark]
        public void ClearEmptyGroup()
        {
            this.textBox.Attributes.Clear();
        }

        [Benchmark]
        public void SetClearGroup()
        {
            this.textBox.Attributes.Add("class", "my-class");
            this.textBox.Attributes.Clear();
        }

        [Benchmark]
        public void ComprehensiveCompositeControl() => InitAndRender(() => {
            var ix = random.Next() & 15; // don't let the branch predictor get too comfy
            var prototype = prototypeCompositeControls[ix];
            var control = new HtmlGenericControl("tr");
            prototype.BuildContent(context, control);
            return control;
        });

        [Benchmark]
        public void RenderLargeTree() => InitAndRender(() => {
            var template1 = new CloneTemplate(
                new HtmlGenericControl("div")
                    .AddCssClasses("class1", "class2")
                    .AddAttribute("data-something", innerCollectionValueBinding)
                    .AppendChildren(
                        new Literal("Hello"),
                        new Literal(innerCollectionValueBinding)
                    )
            );
            var root = new HtmlGenericControl("div");
            var repeater = new Repeater()
                .SetProperty(r => r.ItemTemplate, template1)
                .SetProperty(r => r.DataSource, collectionValueBinding)
                .SetProperty(r => r.WrapperTagName, "div");

            root.AppendChildren(repeater);

            var ul = new HtmlGenericControl("ul");
            var lis = Enumerable.Range(0, 300).Select(i => new HtmlGenericControl("li")
                    .AppendChildren(new Literal($"Item {i}"))
                    .AppendChildren(new HtmlGenericControl("span")
                        .SetAttribute("data-something-else", "lala")
                        .SetAttribute("title", $"bla {i}")
                        .AddCssClass("my-class")
                        .AddCssClass("my-class2")
                        .AddCssClass("my-class3", boolValueBinding)
                        .AppendChildren(new Literal($"content {i}") { RenderSpanElement = false })
                        .AppendChildren(new Literal("content") { RenderSpanElement = true }.AddCssClass("my-class4"))
                    )
                    .AppendChildren(new HtmlGenericControl("br"))
                    .AppendChildren(new HtmlGenericControl("span")
                        .SetAttribute("data-something-else", "lala")
                        .SetAttribute("title", "bla")
                        .AddCssClass("my-class")
                        .AddCssClass("my-class2")
                        .AddCssClass("my-class3", boolValueBinding)
                        .AppendChildren(new Literal($"content") { RenderSpanElement = false })
                        .AppendChildren(new Literal("content") { RenderSpanElement = true }.AddCssClass("my-class4"))
                    )
            ).ToArray();
            random.Shuffle(lis); // cache trolling
            root.AppendChildren(lis);

            return root;
        });


        public class TestCompositeControl: CompositeControl
        {
            public DotvvmControl GetContents(
                TestCapability props,
                bool oneMoreProperty = false
            )
            {
                var div2 = new HtmlGenericControl("span").AddCssClass("name").AddCssClass("blabla", oneMoreProperty).AppendChildren(new Literal("Hello"));
                var div1 = new HtmlGenericControl("td", props.Html)
                    .AddCssClass("my-class1")
                    .AddAttribute("title", "Some text")
                    .AddAttribute("colspan", props.BindableIntProperty)
                    .AppendChildren(div2);

                return div1;
            }
        }

        [DotvvmControlCapability]
        public sealed record TestCapability
        {
            public TextOrContentCapability Text { get; init; }
            public HtmlCapability Html { get; init; }

            [DotvvmControlCapability(prefix: "inner:")]
            public HtmlCapability InnerHtml { get; init; }

            public bool BooleanProperty { get; init; } = true;

            public ValueOrBinding<int>? BindableIntProperty { get; init; }
        }

    }
}
