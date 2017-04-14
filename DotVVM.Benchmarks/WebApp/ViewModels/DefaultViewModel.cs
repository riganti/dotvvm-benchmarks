using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DotVVM.Framework.ViewModel;

namespace DotVVM.Benchmarks.WebApp.ViewModels
{
    public class DefaultViewModel : DotvvmViewModelBase
    {
        public List<SimpleItem> Items { get; set; }

        public DefaultViewModel()
        {
            Items = Enumerable.Range(0, 10000).Select(i => new SimpleItem { Title = "Hello from DotVVM!" }).ToList();
        }

        public void Click(string text)
        {
            Items.Add(new SimpleItem { Title = text });
        }
    }

    public class SimpleItem
    {
        public string Title { get; set; }
    }
}
