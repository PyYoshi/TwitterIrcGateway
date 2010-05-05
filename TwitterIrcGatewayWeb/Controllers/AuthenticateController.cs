using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Ajax;
using System.Web.Routing;
using System.Web.Security;
using OAuth;
using TwitterIrcGatewayWeb;
using Misuzilla.Applications.TwitterIrcGateway.AddIns.SqlServerDataStore;
using System.Configuration;
using TwitterIrcGatewayWeb.Models;

namespace TwitterIrcGatewayWeb.Controllers
{
    public class AuthenticateController : Controller
    {
        //
        // GET: /Authenticate/

        public ActionResult Index()
        {
            return View();
        }
        

        public ActionResult Login(String BackUrl)
        {
            ViewData["BackUrl"] = BackUrl;
            return View();
        }
        
        public ActionResult RequestToken(String BackUrl)
        {
            TwitterOAuth oauth = TwitterUtility.CreateOAuthClient();
            oauth.CallbackUrl = Url.Action("Callback", "Authenticate", new RouteValueDictionary(new {BackUrl = BackUrl}), "http", "localhost");
            return Redirect(oauth.GetAuthorizeUrl());
        }
        
        public ActionResult Callback(String oauth_token, String oauth_verifier, String BackUrl)
        {
            TwitterOAuth oauth = TwitterUtility.CreateOAuthClient();
            TwitterIdentity identity = oauth.RequestAccessToken(oauth_token, oauth_verifier);

            // ”FØCookie‚ð”­s‚·‚é
            FormsAuthenticationTicket ticket = new FormsAuthenticationTicket(1, identity.UserId.ToString(), DateTime.Now,
                                                                             DateTime.Now.AddDays(1), true,
                                                                             identity.ScreenName);
            String encryptedTicket = FormsAuthentication.Encrypt(ticket);
            HttpCookie cookie = new HttpCookie(FormsAuthentication.FormsCookieName, encryptedTicket);
            cookie.Expires = ticket.Expiration;
            Response.Cookies.Add(cookie);
            
            // Token‚ð‹L˜^‚·‚é
            using (TwitterIrcGatewayDataContext ctx = new TwitterIrcGatewayDataContext())
            {
                AuthUser authUser = ctx.AuthUser.Where(x => x.UserId == identity.UserId).FirstOrDefault();
                if (authUser == null)
                {
                    authUser = new AuthUser()
                                   {
                                       UserId = identity.UserId,
                                   };
                    ctx.AuthUser.InsertOnSubmit(authUser);
                }
                authUser.Token = identity.Token;
                authUser.TokenSecret = identity.TokenSecret;
                ctx.SubmitChanges();
            }
            
            if (!String.IsNullOrEmpty(BackUrl) && BackUrl.StartsWith("/"))
            {
                return Redirect(BackUrl);
            }
            
            return Redirect("/");
        }
        
        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            return Redirect("/");
        }

    }
}
