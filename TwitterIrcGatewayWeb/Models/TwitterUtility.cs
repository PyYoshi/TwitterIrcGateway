using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;

namespace TwitterIrcGatewayWeb.Models
{
    public class TwitterUtility
    {
        public static TwitterOAuth CreateOAuthClient()
        {
            return new TwitterOAuth(ConfigurationManager.AppSettings["TwitterOAuthConsumerToken"], ConfigurationManager.AppSettings["TwitterOAuthConsumerTokenSecret"]);
        }
    }
}
