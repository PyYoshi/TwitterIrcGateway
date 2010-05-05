using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Ajax;
using TwitterIrcGatewayWeb;

namespace TwitterIrcGatewayWeb.Controllers
{
    public class ConfigController : Controller
    {
        //
        // GET: /Config/
        [TwitterAuthorize]
        public ActionResult Index()
        {
            return View();
        }

        //
        // GET: /Export/
        [TwitterAuthorize]
        public ActionResult Export(String target)
        {
            if (String.IsNullOrEmpty(target))
                return View();

            String userConfigDir =
                    Path.Combine(
                        Path.Combine(Server.MapPath("/"), @"..\Bin\Debug\Configs\"),
                        ((TwitterIdentity)User.Identity).UserId.ToString());
            if (target == "Config")
                return File(Path.Combine(userConfigDir, "Config.xml"),"text/xml", "Config.xml");
            if (target == "Groups")
                return File(Path.Combine(userConfigDir, "Groups.xml"),"text/xml","Groups.xml");
            
            throw new HttpException(404, "Object Not Found");
        }
    
        //
        // GET: /Import/
        [TwitterAuthorize]
        public ActionResult Import()
        {
            return View();
        }
    
        //
        // POST: /Import/
        [TwitterAuthorize]
        [AcceptVerbs(HttpVerbs.Post)]
        public ActionResult Import(String target, HttpPostedFileBase uploadFile)
        {
            return View();
        }
    }
}
