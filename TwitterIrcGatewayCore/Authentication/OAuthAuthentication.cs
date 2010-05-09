using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using Misuzilla.Net.Irc;

namespace Misuzilla.Applications.TwitterIrcGateway.Authentication
{
    public class OAuthAuthentication : IAuthentication
    {
        public const String ClientKey = "9gGt51Xp3AB8C7wU2Tw";
        public const String SecretKey = "74K9CwKANFVLVupHMtHy4fJ3TjAJq58CvxxtAQjoI";

        #region IAuthentication メンバ
        public AuthenticateResult Authenticate(Server server, Connection connection, UserInfo userInfo)
        {
            // ニックネームとパスワードのチェック
            if (String.IsNullOrEmpty(userInfo.Nick))
            {
                return new AuthenticateResult(ErrorReply.ERR_NONICKNAMEGIVEN, "No nickname given");
            }

            // OAuth ログイン/設定
            if (String.IsNullOrEmpty(userInfo.Password))
            {
                connection.SendGatewayServerMessage("* OAuth 認証の設定を開始します...");
                return new OAuthContinueAuthenticationResult();
            }
            else
            {
                var config = Config.LoadConfig(userInfo.UserName); // HOSTING => 番号的 ID になる

                // xAuth fallback
                if (true && String.IsNullOrEmpty(config.OAuthUserPasswordHash))
                {
                    return new XAuthAuthentication().Authenticate(server, connection, userInfo);
                }

                connection.SendGatewayServerMessage("* アカウント認証を確認しています(OAuth)...");
                // OAuth 設定未設定
                if (String.IsNullOrEmpty(config.OAuthAccessToken) || String.IsNullOrEmpty(config.OAuthTokenSecret))
                {
                    connection.SendGatewayServerMessage("* OAuth 認証の設定を開始します...");
                    return new OAuthContinueAuthenticationResult();
                }

                // 設定してあるパスワードとの照合
                if (Utility.GetMesssageDigest(userInfo.Password) != config.OAuthUserPasswordHash)
                {
                    connection.SendGatewayServerMessage("* アカウント認証に失敗しました。ユーザ名またはパスワードを確認してください。");
                    return new AuthenticateResult(ErrorReply.ERR_PASSWDMISMATCH, "Password Incorrect");
                }

                // ユーザー認証問い合わせをしてみる
                try
                {
                    TwitterOAuth twitterOAuth = new TwitterOAuth(ClientKey, SecretKey)
                    {
                        Token = config.OAuthAccessToken,
                        TokenSecret = config.OAuthTokenSecret
                    };
                    TwitterIdentity identity = new TwitterIdentity
                                                   {
                                                       Token = config.OAuthAccessToken,
                                                       TokenSecret = config.OAuthTokenSecret
                                                   };
                    TwitterService twitterService = new TwitterService(identity);
                    User twitterUser = twitterService.VerifyCredential();
                    identity.ScreenName = twitterUser.ScreenName;
                    identity.UserId = twitterUser.Id;
                    connection.SendGatewayServerMessage(String.Format("* アカウント: {0} (ID:{1})", twitterUser.ScreenName, twitterUser.Id));

                    return new TwitterAuthenticateResult(twitterUser, identity);
                }
                catch (Exception ex)
                {
                    connection.SendServerErrorMessage(TwitterOAuth.GetMessageFromException(ex));
                    return new AuthenticateResult(ErrorReply.ERR_PASSWDMISMATCH, "Password Incorrect");
                }
            }
        }

        #endregion
    }

    public class OAuthContinueAuthenticationResult : AuthenticateResult
    {
        public OAuthContinueAuthenticationResult() : base()
        {
        }
    }
}
