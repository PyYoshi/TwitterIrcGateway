using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using System.IO.Compression;
using System.Linq;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    /// <summary>
    /// Twitterへの接続と操作を提供します。
    /// </summary>
    public class TwitterService : IDisposable
    {
        //private WebClient _webClient;
        private CredentialCache _credential = new CredentialCache();
        private IWebProxy _proxy = WebRequest.DefaultWebProxy;
        private String _userName;
        private Boolean _cookieLoginMode = false;
        private Boolean _enableDropProtection = true;

        private Timer _timer;
        private Timer _timerDirectMessage;
        private Timer _timerReplies;

        private DateTime _lastAccessDirectMessage = DateTime.Now;
        private Int64 _lastAccessTimelineId = 1;
        private Int64 _lastAccessRepliesId = 1;
        private Int64 _lastAccessDirectMessageId = 1;
        private Boolean _isFirstTime = true;
        private Boolean _isFirstTimeReplies = true;
        private Boolean _isFirstTimeDirectMessage = true;

        private LinkedList<Int64> _statusBuffer;
        private LinkedList<Int64> _repliesBuffer;

        #region Events
        /// <summary>
        /// 更新チェック時にエラーが発生した場合に発生します。
        /// </summary>
        public event EventHandler<ErrorEventArgs> CheckError;
        /// <summary>
        /// タイムラインステータスの更新があった場合に発生します。
        /// </summary>
        public event EventHandler<StatusesUpdatedEventArgs> TimelineStatusesReceived;
        /// <summary>
        /// Repliesの更新があった場合に発生します。
        /// </summary>
        public event EventHandler<StatusesUpdatedEventArgs> RepliesReceived;
        /// <summary>
        /// ダイレクトメッセージの更新があった場合に発生します。
        /// </summary>
        public event EventHandler<DirectMessageEventArgs> DirectMessageReceived;
        #endregion

        #region Fields
        /// <summary>
        /// Twitter APIのエンドポイントURLのプレフィックスを取得・設定します。
        /// </summary>
        public String ServiceServerPrefix = "http://api.twitter.com/1";
        /// <summary>
        /// リクエストのRefererを取得・設定します。
        /// </summary>
        public String Referer = "http://twitter.com/home";
        /// <summary>
        /// リクエストのクライアント名を取得・設定します。この値はsourceパラメータとして利用されます。
        /// </summary>
        public String ClientName = "TwitterIrcGateway";
        public String ClientUrl = "http://www.misuzilla.org/dist/net/twitterircgateway/";
        public String ClientVersion = typeof(TwitterService).Assembly.GetName().Version.ToString();
        #endregion

        /// <summary>
        /// TwitterService クラスのインスタンスをユーザ名とパスワードで初期化します。
        /// </summary>
        /// <param name="userName">ユーザー名</param>
        /// <param name="password">パスワード</param>
        [Obsolete]
        public TwitterService(String userName, String password)
        {
            _credential.Add(new Uri(ServiceServerPrefix), "Basic", new NetworkCredential(userName, password));
            _userName = userName;

            Initialize();
        }

        /// <summary>
        /// TwitterService クラスのインスタンスをOAuthを利用する設定で初期化します。
        /// </summary>
        /// <param name="twitterIdentity"></param>
        public TwitterService(String clientKey, String secretKey, TwitterIdentity twitterIdentity)
        {
            OAuthClient = new TwitterOAuth(clientKey, secretKey)
                          {
                              Token = twitterIdentity.Token,
                              TokenSecret = twitterIdentity.TokenSecret
                          };
            _userName = twitterIdentity.ScreenName;

            Initialize();
        }

        private void Initialize()
        {
            _Counter.Increment(ref _Counter.TwitterService);
            _timer = new Timer(new TimerCallback(OnTimerCallback), null, Timeout.Infinite, Timeout.Infinite);
            _timerDirectMessage = new Timer(new TimerCallback(OnTimerCallbackDirectMessage), null, Timeout.Infinite, Timeout.Infinite);
            _timerReplies = new Timer(new TimerCallback(OnTimerCallbackReplies), null, Timeout.Infinite, Timeout.Infinite);

            _statusBuffer = new LinkedList<Int64>();
            _repliesBuffer = new LinkedList<Int64>();

            Interval = 90;
            IntervalDirectMessage = 360;
            IntervalReplies = 120;
            BufferSize = 250;
            EnableCompression = false;
            FriendsPerPageThreshold = 100;

            POSTFetchMode = false;
        }

        ~TwitterService()
        {
            //_Counter.Decrement(ref _Counter.TwitterService);
            Dispose();
        }

        /// <summary>
        /// 接続に利用するプロキシを設定します。
        /// </summary>
        public IWebProxy Proxy
        {
            get
            {
                return _proxy;
                //return _webClient.Proxy;
            }
            set
            {
                _proxy = value;
                //_webClient.Proxy = value;
            }
        }

        /// <summary>
        /// Cookieを利用してログインしてデータにアクセスします。
        /// </summary>
        [Obsolete("Cookieログインによるデータ取得は制限されました。POSTFetchModeを利用してください。")]
        public Boolean CookieLoginMode
        {
            get { return _cookieLoginMode; }
            set { _cookieLoginMode = value; }
        }

        /// <summary>
        /// POSTを利用してログインしてデータにアクセスします。
        /// </summary>
        [Obsolete("POSTによる取得は廃止されました。")]
        public Boolean POSTFetchMode
        {
            get { return false; }
            set { }
        }

        /// <summary>
        /// 取りこぼし防止を有効にするかどうかを指定します。
        /// </summary>
        public Boolean EnableDropProtection
        {
            get { return _enableDropProtection; }
            set { _enableDropProtection = value; }
        }

        /// <summary>
        /// 内部で重複チェックするためのバッファサイズを指定します。
        /// </summary>
        public Int32 BufferSize
        {
            get;
            set;
        }

        /// <summary>
        /// タイムラインをチェックする間隔を指定します。
        /// </summary>
        public Int32 Interval
        {
            get;
            set;
        }

        /// <summary>
        /// ダイレクトメッセージをチェックする間隔を指定します。
        /// </summary>
        public Int32 IntervalDirectMessage
        {
            get;
            set;
        }

        /// <summary>
        /// Repliesをチェックする間隔を指定します。
        /// </summary>
        public Int32 IntervalReplies
        {
            get;
            set;
        }

        /// <summary>
        /// Repliesのチェックを実行するかどうかを指定します。
        /// </summary>
        public Boolean EnableRepliesCheck
        {
            get;
            set;
        }

        /// <summary>
        /// タイムラインの一回の取得につき何件取得するかを指定します。
        /// </summary>
        public Int32 FetchCount
        {
            get;
            set;
        }

        /// <summary>
        /// gzip圧縮を利用するかどうかを指定します。
        /// </summary>
        public Boolean EnableCompression
        {
            get;
            set;
        }

        /// <summary>
        /// フォローしているユーザ一覧を取得する際、次のページが存在するか判断する閾値を指定します。
        /// </summary>
        public Int32 FriendsPerPageThreshold
        {
            get;
            set;
        }

        /// <summary>
        /// OAuthクライアントを取得します。
        /// </summary>
        public TwitterOAuth OAuthClient
        {
            get;
            private set;
        }

        /// <summary>
        /// 認証情報を問い合わせます。
        /// </summary>
        /// <return cref="User">ユーザー情報</returns>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public User VerifyCredential()
        {
            return ExecuteRequest<User>(() =>
            {
                String responseBody = GET("/account/verify_credentials.xml", false);
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    User user = User.Serializer.Deserialize(new StringReader(responseBody)) as User;
                    return user;
                }
            });
        }

        /// <summary>
        /// ステータスを更新します。
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public Status UpdateStatus(String message)
        {
            return UpdateStatus(message, 0);
        }

        /// <summary>
        /// ステータスを更新します。
        /// </summary>
        /// <param name="message"></param>
        /// <param name="inReplyToStatusId"></param>
        public Status UpdateStatus(String message, Int64 inReplyToStatusId)
        {
            String encodedMessage = TwitterService.EncodeMessage(message);
            return ExecuteRequest<Status>(() =>
            {
                String postData = String.Format("status={0}&source={1}{2}", encodedMessage, ClientName, (inReplyToStatusId != 0 ? "&in_reply_to_status_id=" + inReplyToStatusId : ""));
                String responseBody = POST("/statuses/update.xml", postData);
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    Status status = Status.Serializer.Deserialize(new StringReader(responseBody)) as Status;
                    return status;
                }
            });
        }

        /// <summary>
        /// 指定されたユーザにダイレクトメッセージを送信します。
        /// </summary>
        /// <param name="screenName"></param>
        /// <param name="message"></param>
        public void SendDirectMessage(String screenName, String message)
        {
            String encodedMessage = TwitterService.EncodeMessage(message);
            ExecuteRequest(() =>
            {
                String postData = String.Format("user={0}&text={1}", GetUserId(screenName), encodedMessage);
                String responseBody = POST("/direct_messages/new.xml", postData);
            });
        }

        /// <summary>
        /// friendsを取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public User[] GetFriends()
        {
            return GetFriends(1);
        }

        /// <summary>
        /// friendsを取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public User[] GetFriends(Int32 maxPage)
        {
            List<User> users = new List<User>();
            Int64 cursor = -1;
            Int32 page = maxPage;
            return ExecuteRequest<User[]>(() =>
            {
                while (cursor != 0 && page > 0)
                {
                    String responseBody = GET(String.Format("/statuses/friends.xml?cursor={0}&lite=true", cursor));
                    if (NilClasses.CanDeserialize(responseBody))
                    {
                        return users.ToArray();
                    }
                    else
                    {
                        UsersList usersList = UsersList.Serializer.Deserialize(new StringReader(responseBody)) as UsersList;
                        if (usersList == null || usersList.Users == null || usersList.Users.User == null || usersList.Users.User.Length == 0)
                        {
                            return users.ToArray();
                        }
                        else
                        {
                            users.AddRange(usersList.Users.User);
                        }

                        --page;
                        cursor = usersList.NextCursor;
                    }
                }

                return users.ToArray();
            });
        }

        /// <summary>
        /// userを取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public User GetUser(String screenName)
        {
            return ExecuteRequest<User>(() =>
            {
                String responseBody = GET(String.Format("/users/show.xml?screen_name={0}", screenName), false);
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    User user = User.Serializer.Deserialize(new StringReader(responseBody)) as User;
                    return user;
                }
            });
        }
        /// <summary>
        /// 指定したIDでユーザ情報を取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public User GetUserById(Int32 id)
        {
            return ExecuteRequest<User>(() =>
            {
                String responseBody = GET(String.Format("/users/show.xml?id={0}", id), false);
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    User user = User.Serializer.Deserialize(new StringReader(responseBody)) as User;
                    return user;
                }
            });
        }

        /// <summary>
        /// timeline を取得します。
        /// </summary>
        /// <param name="since">最終更新日時</param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Statuses GetTimeline(DateTime since)
        {
            return GetTimeline(since, FetchCount);
        }
        /// <summary>
        /// timeline を取得します。
        /// </summary>
        /// <param name="since">最終更新日時</param>
        /// <param name="count">取得数</param>
        /// <returns></returns>
        public Statuses GetTimeline(DateTime since, Int32 count)
        {
            return ExecuteRequest<Statuses>(() =>
            {
                String responseBody = GET(String.Format("/statuses/friends_timeline.xml?since={0}&count={1}", Utility.UrlEncode(since.ToUniversalTime().ToString("r")), count));
                Statuses statuses;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    statuses = new Statuses();
                    statuses.Status = new Status[0];
                }
                else
                {
                    statuses = Statuses.Serializer.Deserialize(new StringReader(responseBody)) as Statuses;
                    if (statuses == null || statuses.Status == null)
                    {
                        statuses = new Statuses();
                        statuses.Status = new Status[0];
                    }
                }

                return statuses;
            });
        }

        /// <summary>
        /// timeline を取得します。
        /// </summary>
        /// <param name="sinceId">最後に取得したID</param>
        /// <returns>ステータス</returns>
        public Statuses GetTimeline(Int64 sinceId)
        {
            return GetTimeline(sinceId, FetchCount);
        }

        /// <summary>
        /// timeline を取得します。
        /// </summary>
        /// <param name="sinceId">最後に取得したID</param>
        /// <param name="count">取得数</param>
        /// <returns>ステータス</returns>
        public Statuses GetTimeline(Int64 sinceId, Int32 count)
        {
            return ExecuteRequest<Statuses>(() =>
            {
                String responseBody = GET(String.Format("/statuses/home_timeline.xml?since_id={0}&count={1}", sinceId, count));
                Statuses statuses;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    statuses = new Statuses();
                    statuses.Status = new Status[0];
                }
                else
                {
                    statuses = Statuses.Serializer.Deserialize(new StringReader(responseBody)) as Statuses;
                    if (statuses == null || statuses.Status == null)
                    {
                        statuses = new Statuses();
                        statuses.Status = new Status[0];
                    }
                }

                return statuses;
            });
        }

        /// <summary>
        /// 指定したIDでステータスを取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Status GetStatusById(Int64 id)
        {
            return ExecuteRequest<Status>(() =>
            {
                String responseBody = GET(String.Format("/statuses/show.xml?id={0}", id), false);
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    Status status = Status.Serializer.Deserialize(new StringReader(responseBody)) as Status;
                    return status;
                }
            });
        }

        /// <summary>
        /// replies を取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        [Obsolete]
        public Statuses GetReplies()
        {
            return GetMentions();
        }

        /// <summary>
        /// mentions を取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Statuses GetMentions()
        {
            return GetMentions(1);
        }

        /// <summary>
        /// mentions を取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Statuses GetMentions(Int64 sinceId)
        {
            return ExecuteRequest<Statuses>(() =>
            {
                String responseBody = GET(String.Format("/statuses/mentions.xml?since_id={0}", sinceId));
                Statuses statuses;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    statuses = new Statuses();
                    statuses.Status = new Status[0];
                }
                else
                {
                    statuses = Statuses.Serializer.Deserialize(new StringReader(responseBody)) as Statuses;
                    if (statuses == null || statuses.Status == null)
                    {
                        statuses = new Statuses();
                        statuses.Status = new Status[0];
                    }
                }

                return statuses;
            });
        }

        /// <summary>
        /// direct messages を取得します。
        /// </summary>
        /// <param name="since">最終更新日時</param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public DirectMessages GetDirectMessages(DateTime since)
        {
            return ExecuteRequest<DirectMessages>(() =>
            {
                // Cookie ではダメ
                String responseBody = GET(String.Format("/direct_messages.xml?since={0}", Utility.UrlEncode(since.ToUniversalTime().ToString("r"))), false);
                DirectMessages directMessages;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    // 空
                    directMessages = new DirectMessages();
                    directMessages.DirectMessage = new DirectMessage[0];
                }
                else
                {
                    directMessages = DirectMessages.Serializer.Deserialize(new StringReader(responseBody)) as DirectMessages;
                    if (directMessages == null || directMessages.DirectMessage == null)
                    {
                        directMessages = new DirectMessages();
                        directMessages.DirectMessage = new DirectMessage[0];
                    }
                }

                return directMessages;
            });
        }

        /// <summary>
        /// direct messages を取得します。
        /// </summary>
        /// <param name="sinceId">最後に取得したID</param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public DirectMessages GetDirectMessages(Int64 sinceId)
        {
            return ExecuteRequest<DirectMessages>(() =>
            {
                // Cookie ではダメ
                String responseBody = GET(String.Format("/direct_messages.xml?since_id={0}", sinceId), false);
                DirectMessages directMessages;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    // 空
                    directMessages = new DirectMessages();
                    directMessages.DirectMessage = new DirectMessage[0];
                }
                else
                {
                    directMessages = DirectMessages.Serializer.Deserialize(new StringReader(responseBody)) as DirectMessages;
                    if (directMessages == null || directMessages.DirectMessage == null)
                    {
                        directMessages = new DirectMessages();
                        directMessages.DirectMessage = new DirectMessage[0];
                    }
                }

                return directMessages;
            });
        }

        /// <param name="screenName">スクリーンネーム</param>
        /// <param name="since">最終更新日時</param>
        /// <param name="count"></param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Statuses GetTimelineByScreenName(String screenName, DateTime since, Int32 count)
        {
            return ExecuteRequest<Statuses>(() =>
            {
                StringBuilder sb = new StringBuilder();
                if (since != new DateTime())
                    sb.Append("since=").Append(Utility.UrlEncode(since.ToUniversalTime().ToString("r"))).Append("&");
                if (count > 0)
                    sb.Append("count=").Append(count).Append("&");

                String responseBody = GET(String.Format("/statuses/user_timeline.xml?screen_name={0}&{1}", screenName, sb.ToString()));
                Statuses statuses;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    statuses = new Statuses();
                    statuses.Status = new Status[0];
                }
                else
                {
                    statuses = Statuses.Serializer.Deserialize(new StringReader(responseBody)) as Statuses;
                    if (statuses == null || statuses.Status == null)
                    {
                        statuses = new Statuses();
                        statuses.Status = new Status[0];
                    }
                }

                return statuses;
            });
        }

        /// <summary>
        /// 指定したユーザの favorites を取得します。
        /// </summary>
        /// <param name="screenName">スクリーンネーム</param>
        /// <param name="page">ページ</param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Statuses GetFavoritesByScreenName(String screenName, Int32 page)
        {
            return ExecuteRequest<Statuses>(() =>
            {
                StringBuilder sb = new StringBuilder();
                if (page > 0)
                    sb.Append("page=").Append(page).Append("&");

                String responseBody = GET(String.Format("/favorites.xml?screen_name={0}&{1}", screenName, sb.ToString()));
                Statuses statuses;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    statuses = new Statuses();
                    statuses.Status = new Status[0];
                }
                else
                {
                    statuses = Statuses.Serializer.Deserialize(new StringReader(responseBody)) as Statuses;
                    if (statuses == null || statuses.Status == null)
                    {
                        statuses = new Statuses();
                        statuses.Status = new Status[0];
                    }
                }

                return statuses;
            });
        }

        /// <summary>
        /// メッセージをfavoritesに追加します。
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Status CreateFavorite(Int64 id)
        {
            return ExecuteRequest<Status>(() =>
            {
                String responseBody = POST(String.Format("/favorites/create/{0}.xml", id), "");
                Status status;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    status = Status.Serializer.Deserialize(new StringReader(responseBody)) as Status;
                    return status;
                }
            });
        }

        /// <summary>
        /// メッセージをfavoritesから削除します。
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Status DestroyFavorite(Int64 id)
        {
            return ExecuteRequest<Status>(() =>
            {
                String responseBody = POST(String.Format("/favorites/destroy/{0}.xml", id), "");
                Status status;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    status = Status.Serializer.Deserialize(new StringReader(responseBody)) as Status;
                    return status;
                }
            });
        }

        /// <summary>
        /// メッセージを削除します。
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Status DestroyStatus(Int64 id)
        {
            return ExecuteRequest<Status>(() =>
            {
                String responseBody = POST(String.Format("/statuses/destroy/{0}.xml", id), "");
                Status status;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    status = Status.Serializer.Deserialize(new StringReader(responseBody)) as Status;
                    return status;
                }
            });
        }

        /// <summary>
        /// メッセージをretweetします。
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Status RetweetStatus(Int64 id)
        {
            return ExecuteRequest<Status>(() =>
            {
                String responseBody = POST(String.Format("/statuses/retweet/{0}.xml", id), "");
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    Status status = Status.Serializer.Deserialize(new StringReader(responseBody)) as Status;
                    return status;
                }
            });
        }

        /// <summary>
        /// ユーザをfollowします。
        /// </summary>
        /// <param name="screenName"></param>
        /// <returns></returns>
        public User CreateFriendship(String screenName)
        {
            return ExecuteRequest<User>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/friendships/create.xml", postData);
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    return User.Serializer.Deserialize(new StringReader(responseBody)) as User;
                }
            });
        }

        /// <summary>
        /// ユーザをremoveします。
        /// </summary>
        /// <param name="screenName"></param>
        /// <returns></returns>
        public User DestroyFriendship(String screenName)
        {
            return ExecuteRequest<User>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/friendships/destroy.xml", postData);
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    return User.Serializer.Deserialize(new StringReader(responseBody)) as User;
                }
            });
        }

        /// <summary>
        /// ユーザをblockします。
        /// </summary>
        /// <param name="screenName"></param>
        /// <returns></returns>
        public User CreateBlock(String screenName)
        {
            return ExecuteRequest<User>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/blocks/create.xml", postData);
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    return User.Serializer.Deserialize(new StringReader(responseBody)) as User;
                }
            });
        }

        /// <summary>
        /// ユーザへのblockを解除します。
        /// </summary>
        /// <param name="screenName"></param>
        /// <returns></returns>
        public User DestroyBlock(String screenName)
        {
            return ExecuteRequest<User>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/blocks/destroy.xml", postData);
                if (NilClasses.CanDeserialize(responseBody))
                {
                    return null;
                }
                else
                {
                    return User.Serializer.Deserialize(new StringReader(responseBody)) as User;
                }
            });
        }
        #region 内部タイマーイベント
        /// <summary>
        /// Twitter のタイムラインの受信を開始します。
        /// </summary>
        public void Start()
        {
            // HACK: dueTime を指定しないとMonoで動かないことがある
            _timer.Change(0, Interval * 1000);
            _timerDirectMessage.Change(1000, IntervalDirectMessage * 1000);
            if (EnableRepliesCheck)
            {
                _timerReplies.Change(2000, IntervalReplies * 1000);
            }
        }

        /// <summary>
        /// Twitter のタイムラインの受信を停止します。
        /// </summary>
        public void Stop()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _timerDirectMessage.Change(Timeout.Infinite, Timeout.Infinite);
            _timerReplies.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stateObject"></param>
        private void OnTimerCallback(Object stateObject)
        {
            RunCallback(_timer, CheckNewTimeLine);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stateObject"></param>
        private void OnTimerCallbackDirectMessage(Object stateObject)
        {
            RunCallback(_timerDirectMessage, CheckDirectMessage);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stateObject"></param>
        private void OnTimerCallbackReplies(Object stateObject)
        {
            RunCallback(_timerReplies, CheckNewReplies);
        }

        /// <summary>
        /// 既に受信したstatusかどうかをチェックします。既に送信済みの場合falseを返します。
        /// </summary>
        /// <param name="statusBuffer"></param>
        /// <param name="status"></param>
        /// <returns></returns>
        private Boolean ProcessDropProtection(LinkedList<Int64> statusBuffer, Int64 statusId)
        {
            // 差分チェック
            lock (statusBuffer)
            {
                if (statusBuffer.Contains(statusId))
                    return false;

                statusBuffer.AddLast(statusId);
                if (statusBuffer.Count > BufferSize)
                {
                    // 一番古いのを消す
                    statusBuffer.RemoveFirst();
                }
            }

            return true;
        }
        /// <summary>
        /// 最終更新IDを更新します。
        /// </summary>
        /// <param name="sinceId"></param>
        private void UpdateLastAccessStatusId(Status status, ref Int64 sinceId)
        {
            if (ProcessDropProtection(_statusBuffer, status.Id))
            {
                if (_enableDropProtection)
                {
                    // 取りこぼし防止しているときは一番古いID
                    if (status.Id < sinceId)
                    {
                        sinceId = status.Id;
                    }
                }
                else
                {
                    if (status.Id > sinceId)
                    {
                        sinceId = status.Id;
                    }
                }
            }
        }
        
        /// <summary>
        /// ステータスがすでに流されたかどうかをチェックして、流されていない場合に指定されたアクションを実行します。
        /// </summary>
        /// <param name="status"></param>
        /// <param name="action"></param>
        public void ProcessStatus(Status status, Action<Status> action)
        {
            if (ProcessDropProtection(_statusBuffer, status.Id))
            {
                action(status);
                UpdateLastAccessStatusId(status, ref _lastAccessTimelineId);
            }
         }

        /// <summary>
        /// ステータスがすでに流されたかどうかをチェックして、流されていない場合に指定されたアクションを実行します。
        /// </summary>
        /// <param name="statuses"></param>
        /// <param name="action"></param>
        public void ProcessStatuses(Statuses statuses, Action<Statuses> action)
        {
            Statuses tmpStatuses = new Statuses();
            List<Status> statusList = new List<Status>();
            foreach (Status status in statuses.Status)
            {
                ProcessStatus(status, s =>
                {
                    statusList.Add(status);
                    UpdateLastAccessStatusId(status, ref _lastAccessTimelineId);
                });
            }

            if (statusList.Count == 0)
                return;
            tmpStatuses.Status = statusList.ToArray();
            action(tmpStatuses);
        }
        /// <summary>
        /// Repliesステータスがすでに流されたかどうかをチェックして、流されていない場合に指定されたアクションを実行します。
        /// </summary>
        /// <param name="statuses"></param>
        /// <param name="action"></param>
        public void ProcessRepliesStatus(Statuses statuses, Action<Statuses> action)
        {
            Statuses tmpStatuses = new Statuses();
            List<Status> statusList = new List<Status>();
            foreach (Status status in statuses.Status.Where(s => s.Id > _lastAccessRepliesId))
            {
                if (ProcessDropProtection(_repliesBuffer, status.Id) && ProcessDropProtection(_statusBuffer, status.Id))
                {
                    statusList.Add(status);
                }
                UpdateLastAccessStatusId(status, ref _lastAccessTimelineId);
                UpdateLastAccessStatusId(status, ref _lastAccessRepliesId);
            }

            if (statusList.Count == 0)
                return;
            tmpStatuses.Status = statusList.ToArray();
            action(tmpStatuses);
        }

        private void CheckNewTimeLine()
        {
            Boolean friendsCheckRequired = false;
            RunCheck(delegate
            {
                Statuses statuses = GetTimeline(_lastAccessTimelineId);
                Array.Reverse(statuses.Status);
                // 差分チェック
                ProcessStatuses(statuses, (s) =>
                {
                    OnTimelineStatusesReceived(new StatusesUpdatedEventArgs(s, _isFirstTime, friendsCheckRequired));
                });

                if (_isFirstTime || !_enableDropProtection)
                {
                    if (statuses.Status != null && statuses.Status.Length > 0)
                        _lastAccessTimelineId = statuses.Status.Select(s => s.Id).Max();
                }
                _isFirstTime = false;
            });
        }

        private void CheckDirectMessage()
        {
            RunCheck(delegate
            {
                DirectMessages directMessages = (_lastAccessDirectMessageId == 0) ? GetDirectMessages(_lastAccessDirectMessage) : GetDirectMessages(_lastAccessDirectMessageId);
                Array.Reverse(directMessages.DirectMessage);
                foreach (DirectMessage message in directMessages.DirectMessage)
                {
                    // チェック
                    if (message == null || String.IsNullOrEmpty(message.SenderScreenName))
                    {
                        continue;
                    }

                    OnDirectMessageReceived(new DirectMessageEventArgs(message, _isFirstTimeDirectMessage));

                    // 最終更新時刻
                    if (message.Id > _lastAccessDirectMessageId)
                    {
                        _lastAccessDirectMessage = message.CreatedAt;
                        _lastAccessDirectMessageId = message.Id;
                    }
                }
                _isFirstTimeDirectMessage = false;
            });
        }

        private void CheckNewReplies()
        {
            Boolean friendsCheckRequired = false;
            RunCheck(delegate
            {
                Statuses statuses = GetMentions(_lastAccessRepliesId);
                Array.Reverse(statuses.Status);

                // 差分チェック
                ProcessRepliesStatus(statuses, (s) =>
                {
                    // Here I pass dummy, because no matter how the replier flags
                    // friendsCheckRequired, we cannot receive his or her info
                    // through get_friends.
                    OnRepliesReceived(new StatusesUpdatedEventArgs(s, _isFirstTimeReplies, friendsCheckRequired));
                });

                if (_isFirstTimeReplies || !_enableDropProtection)
                {
                    if (statuses.Status != null && statuses.Status.Length > 0)
                        _lastAccessRepliesId = statuses.Status.Select(s => s.Id).Max();
                }
                _isFirstTimeReplies = false;
            });
        }
        #endregion

        #region イベント
        protected virtual void OnCheckError(ErrorEventArgs e)
        {
            FireEvent<ErrorEventArgs>(CheckError, e);
        }
        protected virtual void OnTimelineStatusesReceived(StatusesUpdatedEventArgs e)
        {
            FireEvent<StatusesUpdatedEventArgs>(TimelineStatusesReceived, e);
        }
        protected virtual void OnRepliesReceived(StatusesUpdatedEventArgs e)
        {
            FireEvent<StatusesUpdatedEventArgs>(RepliesReceived, e);
        }
        protected virtual void OnDirectMessageReceived(DirectMessageEventArgs e)
        {
            FireEvent<DirectMessageEventArgs>(DirectMessageReceived, e);
        }
        private void FireEvent<T>(EventHandler<T> eventHandler, T e) where T : EventArgs
        {
            if (eventHandler != null)
                eventHandler(this, e);
        }
        #endregion

        #region ユーティリティメソッド
        /// <summary>
        /// 
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private static String EncodeMessage(String s)
        {
            return Utility.UrlEncode(s);
        }

        /// <summary>
        /// 必要に応じてIDに変換する
        /// </summary>
        /// <param name="screenName"></param>
        /// <returns></returns>
        private String GetUserId(String screenName)
        {
            Int32 id;
            if (Int32.TryParse(screenName, out id))
            {
                return GetUser(screenName).Id.ToString();
            }
            else
            {
                return screenName;
            }
        }
        #endregion

        #region Helper Delegate
        private delegate T ExecuteRequestProcedure<T>();
        private delegate void Procedure();

        private T ExecuteRequest<T>(ExecuteRequestProcedure<T> execProc)
        {
            try
            {
                return execProc();
            }
            catch (WebException)
            {
                throw;
            }
            catch (InvalidOperationException ioe)
            {
                // XmlSerializer
                throw new TwitterServiceException(ioe);
            }
            catch (XmlException xe)
            {
                throw new TwitterServiceException(xe);
            }
            catch (IOException ie)
            {
                throw new TwitterServiceException(ie);
            }
        }

        private void ExecuteRequest(Procedure execProc)
        {
            try
            {
                execProc();
            }
            catch (WebException)
            {
                throw;
            }
            catch (InvalidOperationException ioe)
            {
                // XmlSerializer
                throw new TwitterServiceException(ioe);
            }
            catch (XmlException xe)
            {
                throw new TwitterServiceException(xe);
            }
            catch (IOException ie)
            {
                throw new TwitterServiceException(ie);
            }
        }

        /// <summary>
        /// チェックを実行します。例外が発生した場合には自動的にメッセージを送信します。
        /// </summary>
        /// <param name="proc">実行するチェック処理</param>
        /// <returns></returns>
        private Boolean RunCheck(Procedure proc)
        {
            try
            {
                proc();
            }
            catch (WebException ex)
            {
                HttpWebResponse webResponse = ex.Response as HttpWebResponse;
                if (ex.Response == null || webResponse != null || webResponse.StatusCode != HttpStatusCode.NotModified)
                {
                    // not-modified 以外
                    OnCheckError(new ErrorEventArgs(ex));
                    return false;
                }
            }
            catch (TwitterServiceException ex2)
            {
                try { OnCheckError(new ErrorEventArgs(ex2)); }
                catch { }
                return false;
            }
            catch (Exception ex3)
            {
                try { OnCheckError(new ErrorEventArgs(ex3)); }
                catch { }
                TraceLogger.Twitter.Information("RunCheck(Unhandled Exception): " + ex3.ToString());
                return false;
            }

            return true;
        }

        /// <summary>
        /// タイマーコールバックの処理を実行します。
        /// </summary>
        /// <param name="timer"></param>
        /// <param name="callbackProcedure"></param>
        private void RunCallback(Timer timer, Procedure callbackProcedure)
        {
            // あまりに処理が遅れると二重になる可能性がある
            if (timer != null && Monitor.TryEnter(timer))
            {
                try
                {
                    callbackProcedure();
                }
                finally
                {
                    Monitor.Exit(timer);
                }
            }
        }
        #endregion

        #region IDisposable メンバ

        public void Dispose()
        {
            //if (_webClient != null)
            //{
            //    _webClient.Dispose();
            //    _webClient = null;
            //}

            Stop();

            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
            if (_timerDirectMessage != null)
            {
                _timerDirectMessage.Dispose();
                _timerDirectMessage = null;
            }
            if (_timerReplies != null)
            {
                _timerReplies.Dispose();
                _timerReplies = null;
            }

            GC.SuppressFinalize(this);
            _Counter.Decrement(ref _Counter.TwitterService);
        }

        #endregion

        internal class PreAuthenticatedWebClient : WebClient
        {
            private TwitterService _twitterService;
            public PreAuthenticatedWebClient(TwitterService twitterService)
            {
                _twitterService = twitterService;
            }
            protected override WebRequest GetWebRequest(Uri address)
            {
                // このアプリケーションで HttpWebReqeust 以外がくることはない
                HttpWebRequest webRequest = base.GetWebRequest(address) as HttpWebRequest;
                webRequest.PreAuthenticate = true;
                webRequest.Accept = "text/xml, application/xml";
                webRequest.UserAgent = String.Format("{0}/{1}", _twitterService.ClientName, GetType().Assembly.GetName().Version);
                //webRequest.Referer = TwitterService.Referer;
                webRequest.Headers["X-Twitter-Client"] = _twitterService.ClientName;
                webRequest.Headers["X-Twitter-Client-Version"] = _twitterService.ClientVersion;
                webRequest.Headers["X-Twitter-Client-URL"] = _twitterService.ClientUrl;
                if (_twitterService.EnableCompression)
                    webRequest.Headers["Accept-Encoding"] = "gzip";

                return webRequest;
            }
        }

        /// <summary>
        /// 指定されたURLからデータを取得し文字列として返します。CookieLoginModeが有効なときは自動的にCookieログイン状態で取得します。
        /// </summary>
        /// <param name="url">データを取得するURL</param>
        /// <returns></returns>
        public String GET(String url)
        {
            return GET(url, POSTFetchMode);
        }

        /// <summary>
        /// 指定されたURLからデータを取得し文字列として返します。
        /// </summary>
        /// <param name="url">データを取得するURL</param>
        /// <param name="postFetchMode">POSTで取得するかどうか</param>
        /// <returns></returns>
        public String GET(String url, Boolean postFetchMode)
        {
            TraceLogger.Twitter.Information("GET: " + url);
            if (OAuthClient == null)
            {
                return GETWithBasicAuth(url, postFetchMode);
            }
            else
            {
                return GETWithOAuth(url);
            }
        }

        public String POST(String url, String postData)
        {
            TraceLogger.Twitter.Information("POST: " + url);
            if (OAuthClient == null)
            {
                return POSTWithBasicAuth(url, Encoding.UTF8.GetBytes(postData));
            }
            else
            {
                return OAuthClient.Request(new Uri(ServiceServerPrefix + url), TwitterOAuth.HttpMethod.POST, postData);
            }
        }

        #region Basic 認証アクセス
        private String GETWithBasicAuth(String url, Boolean postFetchMode)
        {
            if (postFetchMode)
            {
                return POST(url, "");
            }
            else
            {
                url = ServiceServerPrefix + url;
                HttpWebRequest webRequest = CreateHttpWebRequest(url, "GET");
                HttpWebResponse webResponse = webRequest.GetResponse() as HttpWebResponse;
                using (StreamReader sr = new StreamReader(GetResponseStream(webResponse)))
                    return sr.ReadToEnd();
            }
        }

        private String POSTWithBasicAuth(String url, Byte[] postData)
        {
            url = ServiceServerPrefix + url;
            HttpWebRequest webRequest = CreateHttpWebRequest(url, "POST");
            using (Stream stream = webRequest.GetRequestStream())
            {
                stream.Write(postData, 0, postData.Length);
            }
            HttpWebResponse webResponse = webRequest.GetResponse() as HttpWebResponse;
            using (StreamReader sr = new StreamReader(GetResponseStream(webResponse)))
                return sr.ReadToEnd();
        }

        //[Obsolete]
        protected virtual HttpWebRequest CreateHttpWebRequest(String url, String method)
        {
            HttpWebRequest webRequest = HttpWebRequest.Create(url) as HttpWebRequest;
            //webRequest.Credentials = _credential;
            //webRequest.PreAuthenticate = true;
            webRequest.ServicePoint.Expect100Continue = false;
            webRequest.Proxy = _proxy;
            webRequest.Method = method;
            webRequest.Accept = "text/xml, application/xml, text/html;q=0.5";
            webRequest.UserAgent = String.Format("{0}/{1}", ClientName, ClientVersion);
            //webRequest.Referer = TwitterService.Referer;
            webRequest.Headers["X-Twitter-Client"] = ClientName;
            webRequest.Headers["X-Twitter-Client-Version"] = ClientVersion;
            webRequest.Headers["X-Twitter-Client-URL"] = ClientUrl;

            if (EnableCompression)
                webRequest.Headers["Accept-Encoding"] = "gzip";

            Uri uri = new Uri(url);

            NetworkCredential cred = _credential.GetCredential(uri, "Basic");
            webRequest.Headers["Authorization"] = String.Format("Basic {0}", Convert.ToBase64String(Encoding.UTF8.GetBytes(String.Format("{0}:{1}", cred.UserName, cred.Password))));

            return webRequest as HttpWebRequest;
        }
        #endregion

        #region OAuth 認証アクセス
        private String GETWithOAuth(String url)
        {
            HttpWebRequest webRequest = OAuthClient.CreateRequest(new Uri(ServiceServerPrefix + url), TwitterOAuth.HttpMethod.GET);
            if (EnableCompression)
                webRequest.Headers["Accept-Encoding"] = "gzip";

            HttpWebResponse webResponse = webRequest.GetResponse() as HttpWebResponse;
            using (StreamReader sr = new StreamReader(GetResponseStream(webResponse)))
                return sr.ReadToEnd();
        }
        #endregion

        private Stream GetResponseStream(WebResponse webResponse)
        {
            HttpWebResponse httpWebResponse = webResponse as HttpWebResponse;
            if (httpWebResponse == null)
                return webResponse.GetResponseStream();
            if (String.Compare(httpWebResponse.ContentEncoding, "gzip", true) == 0)
                return new GZipStream(webResponse.GetResponseStream(), CompressionMode.Decompress);
            return webResponse.GetResponseStream();
        }

        #region Cookie アクセス

        private CookieCollection _cookies = null;
        [Obsolete("Cookieによる認証はサポートされません。代わりにGET(POST)を利用してください。")]
        public String GETWithCookie(String url)
        {
            Boolean isRetry = false;
            url = ServiceServerPrefix + url;
        Retry:
            try
            {
                TraceLogger.Twitter.Information("GET(Cookie): {0}", url);
                return DownloadString(url);
            }
            catch (WebException we)
            {
                HttpWebResponse wResponse = we.Response as HttpWebResponse;
                if (wResponse == null || wResponse.StatusCode != HttpStatusCode.Unauthorized || isRetry)
                    throw;

                _cookies = CookieLogin();

                isRetry = true;
                goto Retry;
            }
        }

        [Obsolete("Cookieによる認証はサポートされません。")]
        public CookieCollection CookieLogin()
        {
            TraceLogger.Twitter.Information("Cookie Login: {0}", _userName);

            HttpWebRequest request = CreateWebRequest("http://twitter.com/account/verify_credentials.xml") as HttpWebRequest;
            request.AllowAutoRedirect = false;
            request.Method = "GET";

            NetworkCredential cred = _credential.GetCredential(new Uri("http://twitter.com/account/verify_credentials.xml"), "Basic");
            request.Headers["Authorization"] = String.Format("Basic {0}", Convert.ToBase64String(Encoding.UTF8.GetBytes(String.Format("{0}:{1}", cred.UserName, cred.Password))));

            HttpWebResponse response = request.GetResponse() as HttpWebResponse;
            using (StreamReader sr = new StreamReader(GetResponseStream(response), Encoding.UTF8))
            {
                String responseBody = sr.ReadToEnd();

                if (response.Cookies.Count == 0)
                {
                    throw new ApplicationException("ログインに失敗しました。ユーザ名またはパスワードが間違っている可能性があります。");
                }

                foreach (Cookie cookie in response.Cookies)
                {
                    cookie.Domain = "twitter.com";
                }

                _cookies = response.Cookies;

                return response.Cookies;
            }
        }

        [Obsolete("Cookieによる認証はサポートされません。")]
        WebRequest CreateWebRequest(String uri)
        {
            WebRequest request = WebRequest.Create(uri);
            if (request is HttpWebRequest)
            {
                HttpWebRequest httpRequest = request as HttpWebRequest;
                httpRequest.UserAgent = "Mozilla/4.0 (compatible; MSIE 7.0; Windows NT 5.1)";
                httpRequest.Referer = Referer;
                httpRequest.PreAuthenticate = false;
                httpRequest.Accept = "*/*";
                httpRequest.CookieContainer = new CookieContainer();
                httpRequest.Proxy = _proxy;

                if (EnableCompression)
                    httpRequest.Headers["Accept-Encoding"] = "gzip";

                if (_cookies != null)
                {
                    httpRequest.CookieContainer.Add(_cookies);
                }
            }
            return request;
        }

        String DownloadString(String url)
        {
            WebRequest request = CreateWebRequest(url);
            WebResponse response = null;
            try
            {
                response = request.GetResponse();
                using (StreamReader sr = new StreamReader(GetResponseStream(response)))
                {
                    return sr.ReadToEnd();
                }
            }
            finally
            {
                if (response != null)
                {
                    response.Close();
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// エラー発生時のイベントのデータを提供します。
    /// </summary>
    public class ErrorEventArgs : EventArgs
    {
        public Exception Exception { get; set; }
        public ErrorEventArgs(Exception ex)
        {
            this.Exception = ex;
        }
    }

    /// <summary>
    /// ステータスが更新時のイベントのデータを提供します。
    /// </summary>
    public class StatusesUpdatedEventArgs : EventArgs
    {
        public Statuses Statuses { get; set; }
        public Boolean IsFirstTime { get; set; }
        public Boolean FriendsCheckRequired { get; set; }
        public StatusesUpdatedEventArgs(Statuses statuses)
        {
            this.Statuses = statuses;
        }
        public StatusesUpdatedEventArgs(Statuses statuses, Boolean isFirstTime, Boolean friendsCheckRequired)
            : this(statuses)
        {
            this.IsFirstTime = isFirstTime;
            this.FriendsCheckRequired = friendsCheckRequired;
        }
    }
    /// <summary>
    /// ダイレクトメッセージを受信時のイベントのデータを提供します。
    /// </summary>
    public class DirectMessageEventArgs : EventArgs
    {
        public DirectMessage DirectMessage { get; set; }
        public Boolean IsFirstTime { get; set; }
        public DirectMessageEventArgs(DirectMessage directMessage)
        {
            this.DirectMessage = directMessage;
        }
        public DirectMessageEventArgs(DirectMessage directMessage, Boolean isFirstTime)
            : this(directMessage)
        {
            this.IsFirstTime = isFirstTime;
        }
    }
    /// <summary>
    /// Twitterにアクセスを試みた際にスローされる例外。
    /// </summary>
    public class TwitterServiceException : ApplicationException
    {
        public TwitterServiceException(String message)
            : base(message)
        {
        }
        public TwitterServiceException(Exception innerException)
            : base(innerException.Message, innerException)
        {
        }
        public TwitterServiceException(String message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// データが空を表します。
    /// </summary>
    [XmlRoot("nilclasses")]
    public class NilClasses
    {
        private static Object _syncObject = new object();
        private static XmlSerializer _serializer = null;
        static NilClasses()
        {
            lock (_syncObject)
            {
                if (_serializer == null)
                {
                    _serializer = new XmlSerializer(typeof(NilClasses));
                }
            }
        }
        public static XmlSerializer Serializer
        {
            get
            {
                return _serializer;
            }
        }

        public static Boolean CanDeserialize(String xml)
        {
            return NilClasses.Serializer.CanDeserialize(new XmlTextReader(new StringReader(xml)));
        }
    }

    /// <summary>
    /// DirectMessage のセットを格納します。
    /// </summary>
    [XmlRoot("direct-messages")]
    public class DirectMessages
    {
        [XmlElement("direct_message")]
        public DirectMessage[] DirectMessage;

        private static Object _syncObject = new object();
        private static XmlSerializer _serializer = null;
        static DirectMessages()
        {
            lock (_syncObject)
            {
                if (_serializer == null)
                {
                    _serializer = new XmlSerializer(typeof(DirectMessages));
                }
            }
        }
        public static XmlSerializer Serializer
        {
            get
            {
                return _serializer;
            }
        }
        public DirectMessages()
        {
            _Counter.Increment(ref _Counter.DirectMessages);
        }
        ~DirectMessages()
        {
            _Counter.Decrement(ref _Counter.DirectMessages);
        }
    }

    /// <summary>
    /// ダイレクトメッセージの情報を表します。
    /// </summary>
    [XmlType("DirectMessage")]
    public class DirectMessage
    {
        [XmlElement("id")]
        public Int64 Id;
        [XmlElement("text")]
        public String _text;
        [XmlElement("sender_id")]
        public String SenderId;
        [XmlElement("recipient_id")]
        public String RecipientId;
        [XmlElement("created_at")]
        public String _createdAt;
        [XmlElement("sender_screen_name")]
        public String SenderScreenName;
        [XmlElement("recipient_screen_name")]
        public String RecipientScreenName;

        [XmlIgnore]
        public String Text
        {
            get
            {
                if (String.IsNullOrEmpty(_text))
                    return String.Empty;

                return Utility.UnescapeCharReference(_text);
            }
        }
        [XmlIgnore]
        public DateTime CreatedAt
        {
            get
            {
                return Utility.ParseDateTime(_createdAt);
            }
        }
        public DirectMessage()
        {
            _Counter.Increment(ref _Counter.DirectMessage);
        }
        ~DirectMessage()
        {
            _Counter.Decrement(ref _Counter.DirectMessage);
        }

        public override string ToString()
        {
            return String.Format("DirectMessage: {0} (ID:{1})", Text, Id.ToString());
        }
    }

    /// <summary>
    /// Statusのセットを格納します。
    /// </summary>
    [XmlType("statuses")]
    public class Statuses
    {
        [XmlElement("status")]
        public Status[] Status;

        private static Object _syncObject = new object();
        private static XmlSerializer _serializer = null;
        static Statuses()
        {
            lock (_syncObject)
            {
                if (_serializer == null)
                {
                    _serializer = new XmlSerializer(typeof(Statuses));
                }
            }
        }

        public static XmlSerializer Serializer
        {
            get
            {
                return _serializer;
            }
        }
        public Statuses()
        {
            _Counter.Increment(ref _Counter.Statuses);
        }
        ~Statuses()
        {
            _Counter.Decrement(ref _Counter.Statuses);
        }
    }

    /// <summary>
    /// Userのセットを格納します。
    /// </summary>
    [XmlType("users_list")]
    public class UsersList
    {
        [XmlElement("users")]
        public Users Users { get; set; }

        [XmlElement("next_cursor")]
        public Int64 NextCursor { get; set; }

        [XmlElement("previous_cursor")]
        public Int64 PreviousCursor { get; set; }

        private static Object _syncObject = new object();
        private static XmlSerializer _serializer = null;
        static UsersList()
        {
            lock (_syncObject)
            {
                if (_serializer == null)
                {
                    _serializer = new XmlSerializer(typeof(UsersList));
                }
            }
        }
        public static XmlSerializer Serializer
        {
            get
            {
                return _serializer;
            }
        }
    }

    /// <summary>
    /// Userのセットを格納します。
    /// </summary>
    [XmlType("users")]
    public class Users
    {
        [XmlElement("user")]
        public User[] User;

        private static Object _syncObject = new object();
        private static XmlSerializer _serializer = null;
        static Users()
        {
            lock (_syncObject)
            {
                if (_serializer == null)
                {
                    _serializer = new XmlSerializer(typeof(Users));
                }
            }
        }
        public static XmlSerializer Serializer
        {
            get
            {
                return _serializer;
            }
        }
        public Users()
        {
            _Counter.Increment(ref _Counter.Users);
        }
        ~Users()
        {
            _Counter.Decrement(ref _Counter.Users);
        }
    }

    [XmlType("user")]
    public class User
    {
        [XmlElement("id")]
        public Int32 Id;
        [XmlElement("name")]
        public String Name;
        [XmlElement("screen_name")]
        public String ScreenName;
        [XmlElement("location")]
        public String Location;
        [XmlElement("description")]
        public String Description;
        [XmlElement("profile_image_url")]
        public String ProfileImageUrl;
        [XmlElement("url")]
        public String Url;
        [XmlElement("protected")]
        public Boolean Protected;
        [XmlElement("status")]
        public Status Status;

        private static Object _syncObject = new object();
        private static XmlSerializer _serializer = null;
        static User()
        {
            lock (_syncObject)
            {
                if (_serializer == null)
                {
                    _serializer = new XmlSerializer(typeof(User));
                }
            }
        }
        public static XmlSerializer Serializer
        {
            get
            {
                return _serializer;
            }
        }


        public User()
        {
            _Counter.Increment(ref _Counter.User);
        }
        ~User()
        {
            _Counter.Decrement(ref _Counter.User);
        }

        public override string ToString()
        {
            return String.Format("User: {0} / {1} (ID:{2})", ScreenName, Name, Id.ToString());
        }
    }

    /// <summary>
    /// ステータスを表します。
    /// </summary>
    [XmlType("status")]
    public class Status
    {
        [XmlElement("created_at")]
        public String _createdAtOriginal;
        [XmlElement("id")]
        public Int64 Id;
        [XmlElement("in_reply_to_status_id")]
        public String InReplyToStatusId;
        [XmlElement("in_reply_to_user_id")]
        public String InReplyToUserId;
        [XmlElement("retweeted_status")]
        public Status RetweetedStatus;
        [XmlElement("text")]
        public String _textOriginal;
        [XmlElement("user")]
        public User User;
        [XmlElement("source")]
        public String Source;
        [XmlElement("favorited")]
        public String Favorited;
        [XmlElement("truncated")]
        public Boolean Truncated;

        [XmlIgnore]
        private String _text;
        [XmlIgnore]
        private DateTime _createdAt;

        [XmlIgnore]
        public String Text
        {
            get
            {
                if (!String.IsNullOrEmpty(_textOriginal) && _text == null)
                {
                    _text = Utility.UnescapeCharReference(_textOriginal);
                }

                return _text ?? "";
            }
            set
            {
                _text = value;
            }
        }
        [XmlIgnore]
        public DateTime CreatedAt
        {
            get
            {
                if (!String.IsNullOrEmpty(_createdAtOriginal) && _createdAt == DateTime.MinValue)
                {
                    _createdAt = Utility.ParseDateTime(_createdAtOriginal);
                }
                return _createdAt;
            }
            set
            {
                _createdAt = value;
            }
        }

        private static Object _syncObject = new object();
        private static XmlSerializer _serializer = null;
        static Status()
        {
            lock (_syncObject)
            {
                if (_serializer == null)
                {
                    _serializer = new XmlSerializer(typeof(Status));
                }
            }
        }

        public Status()
        {
            _Counter.Increment(ref _Counter.Status);
        }
        ~Status()
        {
            _Counter.Decrement(ref _Counter.Status);
        }

        public static XmlSerializer Serializer
        {
            get
            {
                return _serializer;
            }
        }

        public override int GetHashCode()
        {
            return (Int32)(this.Id - Int32.MaxValue);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Status))
                return false;

            Status status = obj as Status;
            return (status.Id == this.Id) && (status.Text == this.Text);
        }

        public override string ToString()
        {
            return String.Format("Status: {0} (ID:{1})", Text, Id.ToString());
        }
    }
}
