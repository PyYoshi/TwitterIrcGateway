using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace TwitterIrcGatewayWeb
{
    public class TwitterAuthorizeAttribute : AuthorizeAttribute
    {
        protected override bool AuthorizeCore(System.Web.HttpContextBase httpContext)
        {
            return (httpContext.User.Identity != null) && httpContext.User.Identity.IsAuthenticated;
        }

        public override void OnAuthorization(AuthorizationContext filterContext)
        {
            if (!AuthorizeCore(filterContext.HttpContext))
            {
                filterContext.Result =
                    new RedirectToRouteResult(
                        new RouteValueDictionary(new {Controller = "Authenticate", Action = "Login", BackUrl = filterContext.HttpContext.Request.RawUrl}));
            }
        }
    }
}
