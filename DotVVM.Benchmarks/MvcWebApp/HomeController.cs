using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotVVM.Benchmarks.WebApp.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DotVVM.Benchmarks.MvcWebApp
{
    public class HomeController: Controller
    {
        public IActionResult Index()
        {
            return View(new DefaultViewModel().Items.Select(t => t.Title).ToList());
        }
    }
}
