using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Ajax;
using Misuzilla.Applications.TwitterIrcGateway.AddIns.SqlServerDataStore;
using TwitterIrcGatewayWeb.Models;

namespace TwitterIrcGatewayWeb.Controllers
{
    public class TimelineController : Controller
    {
        //
        // GET: /Timeline/
        //
        [TwitterAuthorize]
        public ActionResult Index()
        {
            ITimelineRepository repos = new TimelineRepository();
            return View(repos.FindByUserId(((TwitterIdentity)User.Identity).UserId));
        }

    }
}
