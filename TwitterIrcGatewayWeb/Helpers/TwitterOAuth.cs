using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
using System.Net;
using OAuth;
using System.Security.Principal;

namespace TwitterIrcGatewayWeb
{
    public class TwitterIdentity : MarshalByRefObject, IIdentity
    {
        public String ScreenName { get; set; }
        public Int32 UserId { get; set; }
        public String Token { get; set; }
        public String TokenSecret { get; set; }
        
        #region IIdentity メンバ
        public string AuthenticationType
        {
            get { return "OAuth"; }
        }

        public bool IsAuthenticated
        {
            get { return true; }
        }

        public string Name
        {
            get { return ScreenName; }
        }
        #endregion
    }
    
    public class TwitterOAuth : OAuthBase
    {
        private String _consumerKey;
        private String _consumerSecret;
        private static readonly Uri RequestTokenUrl = new Uri("http://twitter.com/oauth/request_token");
        private static readonly Uri AuthorizeUrl = new Uri("http://twitter.com/oauth/authorize");
        private static readonly Uri AccessTokenUrl = new Uri("http://twitter.com/oauth/access_token");

        public String Token { get; set; }
        public String TokenSecret { get; set; }
        
        public enum HttpMethod
        {
            GET, POST
        }

        public TwitterOAuth(String consumerKey, String consumerSecret)
        {
            _consumerKey = consumerKey;
            _consumerSecret = consumerSecret;
        }

        #region Step 1 (Request Unauthorized Token)
        public String GetAuthorizeUrl()
        {
            return AuthorizeUrl + "?oauth_token=" + RequestUnauthorizedToken();
        }
    
        public String RequestUnauthorizedToken()
        {
            String requestUrl, queryString;
            String result = Request(RequestTokenUrl, HttpMethod.GET);
            NameValueCollection returnValues = HttpUtility.ParseQueryString(result);

            return returnValues["oauth_token"];
        }
        #endregion

        #region Step 2 (Request Access Token & Setup TwitterOAuth Client)
        public TwitterIdentity RequestAccessToken(String authToken, String verifier)
        {
            Verifier = verifier;
            String result = Request(AccessTokenUrl, HttpMethod.GET, authToken, String.Empty);
            NameValueCollection returnValues = HttpUtility.ParseQueryString(result);

            TwitterIdentity identity = new TwitterIdentity()
                                           {
                                               Token = returnValues["oauth_token"],
                                               TokenSecret = returnValues["oauth_token_secret"],
                                               ScreenName = returnValues["screen_name"],
                                               UserId = Int32.Parse(returnValues["user_id"])
                                           };
            return identity;
        }
        #endregion

        private String Request(Uri requestUrl, HttpMethod method)
        {
            return Request(requestUrl, method, Token, TokenSecret);
        }

        private String Request(Uri requestUrl, HttpMethod method, String token, String tokenSecret)
        {
            String normalizedUrl, queryString;

            String signature = GenerateSignature(requestUrl,
                                                 _consumerKey,
                                                 _consumerSecret,
                                                 token,
                                                 tokenSecret,
                                                 method.ToString(),
                                                 GenerateTimeStamp(),
                                                 GenerateNonce(),
                                                 out normalizedUrl,
                                                 out queryString);

            queryString += "&oauth_signature=" + HttpUtility.UrlEncode(signature);

            UriBuilder uriBuilder = new UriBuilder(normalizedUrl)
                                        {
                                            Query = queryString
                                        };

            using (WebClientEx webClient = new WebClientEx())
            {
                String retVal;
                if (method == HttpMethod.GET)
                {
                    retVal = webClient.DownloadString(uriBuilder.Uri);
                }
                else
                {
                    retVal = webClient.UploadString(uriBuilder.Uri, "POST", queryString);
                }
                return retVal;
            }
        }    
        private class WebClientEx : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                HttpWebRequest webRequest = base.GetWebRequest(address) as HttpWebRequest;
                return webRequest;
            }
        }
    }
}
