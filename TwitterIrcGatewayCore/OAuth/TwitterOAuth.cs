using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using DotNetOpenAuth.Messaging;
using DotNetOpenAuth.OAuth;
using DotNetOpenAuth.OAuth.ChannelElements;
using System.Security.Principal;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    public class TwitterIdentity : MarshalByRefObject, IIdentity
    {
        public String ScreenName { get; set; }
        public Int64 UserId { get; set; }
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

    public class TwitterOAuth
    {
        private IConsumerTokenManager _tokenManager;
        public string ConsumerKey { get; set; }
        public string ConsumerSecret { get; set; }
        public DesktopConsumer Consumer { get; set; }
        private string _requestToken = string.Empty;
        private ServiceProviderDescription _providerDescription = new ServiceProviderDescription
            {
                RequestTokenEndpoint = new MessageReceivingEndpoint("https://api.twitter.com/oauth/request_token", HttpDeliveryMethods.PostRequest),
                UserAuthorizationEndpoint = new MessageReceivingEndpoint("https://api.twitter.com/oauth/authorize", HttpDeliveryMethods.GetRequest),
                AccessTokenEndpoint = new MessageReceivingEndpoint("https://api.twitter.com/oauth/access_token", HttpDeliveryMethods.GetRequest),
                TamperProtectionElements = new ITamperProtectionChannelBindingElement[] 
                {
                    new HmacSha1SigningBindingElement()
                }
            };

        public TwitterOAuth(IConsumerTokenManager tokenManager)
        {
            _tokenManager = tokenManager;
            ConsumerKey = _tokenManager.ConsumerKey;
            ConsumerSecret = _tokenManager.ConsumerSecret;
            Consumer = new DesktopConsumer(_providerDescription, _tokenManager);
            return;
        }

        public TwitterOAuth(string consumerKey, string consumerSecret)
        {
            ConsumerKey = consumerKey;
            ConsumerSecret = consumerSecret;
            _tokenManager = new InMemoryTokenManager(consumerKey, consumerSecret);
            Consumer = new DesktopConsumer(_providerDescription, _tokenManager);
            return;
        }

        public string GetAuthorizeUrl()
        { 
            var requestArgs = new Dictionary<string, string>();
            return Consumer.RequestUserAuthorization(requestArgs, null, out _requestToken).AbsoluteUri;
        }

        public TwitterIdentity RequestAccessToken(string verifier)
        {
            var response = Consumer.ProcessUserAuthorization(_requestToken, verifier);
            TwitterIdentity identity = new TwitterIdentity()
            {
                Token = response.AccessToken,
                TokenSecret = _tokenManager.GetTokenSecret(response.AccessToken),
                ScreenName = response.ExtraData["screen_name"],
                UserId = Int64.Parse(response.ExtraData["user_id"])
            };
            return identity;
        }

        public HttpWebRequest Request(MessageReceivingEndpoint endpoint, string accessToken)
        {
            return Consumer.PrepareAuthorizedRequest(endpoint, accessToken);
        }

        public HttpWebRequest Request(MessageReceivingEndpoint endpoint, string accessToken, IDictionary<string, string> parts)
        {
            return Consumer.PrepareAuthorizedRequest(endpoint, accessToken, parts);
        }

        public static String GetMessageFromException(Exception e)
        {
            if (e is WebException)
            {
                using (HttpWebResponse webResponse = (e as WebException).Response as HttpWebResponse)
                {
                    try
                    {
                        var body = new StreamReader(webResponse.GetResponseStream()).ReadToEnd();
                        // TODO: エラーレスポンスをパースする処理
                        return body;
                    }
                    catch (IOException)
                    { }
                }
            }
            return e.Message;
        }

    }

    
}
