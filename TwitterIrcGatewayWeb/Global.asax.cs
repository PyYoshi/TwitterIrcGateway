using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Security;
using System.Security.Principal;
using Misuzilla.Applications.TwitterIrcGateway.AddIns.SqlServerDataStore;

namespace TwitterIrcGatewayWeb
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801

    public class MvcApplication : System.Web.HttpApplication
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                "Export",                                              // Route name
                "Config/Export/{target}",                           // URL with parameters
                new { controller = "Config", action = "Export", target = "" }  // Parameter defaults
            );

            routes.MapRoute(
                "Default",                                              // Route name
                "{controller}/{action}/{id}",                           // URL with parameters
                new { controller = "Home", action = "Index", id = "" }  // Parameter defaults
            );

        }

        protected void Application_Start()
        {
            RegisterRoutes(RouteTable.Routes);

        }
        
        protected void Application_AuthenticateRequest(object o, EventArgs e)
        {
            HttpCookie cookie = Request.Cookies[FormsAuthentication.FormsCookieName];
            if (cookie != null)
            {
                FormsAuthenticationTicket ticket = FormsAuthentication.Decrypt(cookie.Value);
                using (TwitterIrcGatewayDataContext ctx = new TwitterIrcGatewayDataContext())
                {
                    Int32 userId = Int32.Parse(ticket.Name);
                    AuthUser user = ctx.AuthUser.Where(x => x.UserId == userId).FirstOrDefault();
                    if (user != null)
                    {
                        TwitterIdentity identity = new TwitterIdentity()
                                                       {
                                                           UserId = user.UserId,
                                                           ScreenName = ticket.UserData,
                                                           Token = user.Token,
                                                           TokenSecret = user.TokenSecret
                                                       };
                        GenericPrincipal principal = new GenericPrincipal(identity, new string[] {"User"});
                        HttpContext.Current.User = principal;
                    }
                }
            }
        }
    }
}