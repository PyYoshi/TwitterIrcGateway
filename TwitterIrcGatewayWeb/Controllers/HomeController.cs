using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Misuzilla.Applications.TwitterIrcGateway.AddIns.SqlServerDataStore;

namespace TwitterIrcGatewayWeb.Controllers
{
    [HandleError]
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            using (TwitterIrcGatewayDataContext ctx = new TwitterIrcGatewayDataContext())
            {
                ctx.Connection.Open();
            }
            ViewData["Message"] = "Welcome to ASP.NET MVC!";

            return View();
        }

        public ActionResult About()
        {
            return View();
        }
    }
}
