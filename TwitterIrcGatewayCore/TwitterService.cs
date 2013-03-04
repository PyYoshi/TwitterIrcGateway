using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

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
        public String ServiceServerPrefix = "https://api.twitter.com/1.1";
        /// <summary>
        /// リクエストのRefererを取得・設定します。
        /// </summary>
        public String Referer = "https://twitter.com/home";
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
            EnableDropProtection = true;
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
        /// 取りこぼし防止を有効にするかどうかを指定します。
        /// </summary>
        public Boolean EnableDropProtection
        {
#if HOSTING
            get;
            set;
#else
            get;
            set;
#endif
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
                String responseBody = GET("/account/verify_credentials.json");
                User user = JsonConvert.DeserializeObject<User>(responseBody);
                return user;
            });
        }

        /// <summary>
        /// ステータスを更新します。
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public Tweet UpdateStatus(String message)
        {
            return UpdateStatus(message, 0);
        }

        /// <summary>
        /// ステータスを更新します。
        /// </summary>
        /// <param name="message"></param>
        /// <param name="inReplyToStatusId"></param>
        public Tweet UpdateStatus(String message, Int64 inReplyToStatusId)
        {
            String encodedMessage = TwitterService.EncodeMessage(message);
            return ExecuteRequest<Tweet>(() =>
            {
                String postData = String.Format("status={0}{1}", encodedMessage, (inReplyToStatusId != 0 ? "&in_reply_to_status_id=" + inReplyToStatusId : ""));
                String responseBody = POST("/statuses/update.json", postData);
                Tweet tweet = JsonConvert.DeserializeObject<Tweet>(responseBody);
                return tweet;
            });
        }

        /// <summary>
        /// 指定されたユーザにダイレクトメッセージを送信します。
        /// 
        /// </summary>
        public DirectMessage SendDirectMessage(String screenName, String message)
        {
            String encodedMessage = TwitterService.EncodeMessage(message);
            return ExecuteRequest<DirectMessage>(() =>
            {
                String postData = String.Format("user={0}&text={1}", GetUserId(screenName), encodedMessage);
                String responseBody = POST("/direct_messages/new.json", postData);
                DirectMessage directMessage = JsonConvert.DeserializeObject<DirectMessage>(responseBody);
                return directMessage;
            });
        }

        /// <summary>
        /// 
        /// </summary>
        public UserIds GetFriendIds()
        {
            return GetFriendIds(-1);
        }

        /// <summary>
        /// 
        /// </summary>
        public UserIds GetFriendIds(long cursor)
        {
            UserIds userIds = new UserIds();
            return ExecuteRequest<UserIds>(() =>
            {
                String responseBody = GET(String.Format("/friends/ids.json?cursor={0}", cursor));
                userIds = JsonConvert.DeserializeObject<UserIds>(responseBody);
                return userIds;
            });
            
        }

        /// <summary>
        /// 
        /// </summary>
        public UserIds GetFollowerIds()
        {
            return GetFollowerIds(-1);
        }

        /// <summary>
        /// 
        /// </summary>
        public UserIds GetFollowerIds(long cursor)
        {
            UserIds userIds = new UserIds();
            return ExecuteRequest<UserIds>(() =>
            {
                String responseBody = GET(String.Format("/followers/ids.json?cursor={0}", cursor));
                userIds = JsonConvert.DeserializeObject<UserIds>(responseBody);
                return userIds;
            });
        }

        /// <summary>
        /// usersを取得します。
        /// </summary>
        public List<User> GetUsers(List<long> userIds)
        {
            List<User> users = new List<User>();
            return ExecuteRequest<List<User>>(() =>
            {
                String userIdsStr = JsonConvert.SerializeObject(userIds);
                String postData = String.Format("user_id={0}", userIdsStr);
                String responseBody = POST("/users/lookup.json", postData);
                users = JsonConvert.DeserializeObject<List<User>>(responseBody);
                return users;
            });
        }

        /// <summary>
        /// friendsを取得します。
        /// </summary>
        public List<User> GetFriends()
        {
            List<User> users = new List<User>();
            UserIds userIds = GetFriendIds();
            return GetUsers(userIds.Ids);
        }

        /// <summary>
        /// followersを取得します。
        /// </summary>
        public List<User> GetFollowers()
        {
            List<User> users = new List<User>();
            UserIds userIds = GetFollowerIds();
            return GetUsers(userIds.Ids);
        }

        /// <summary>
        /// userを取得します。
        /// </summary>
        public User GetUser(String screenName)
        {
            return ExecuteRequest<User>(() =>
            {
                String responseBody = GET(String.Format("/users/show.json?screen_name={0}&include_entities=true", screenName));
                User user = JsonConvert.DeserializeObject<User>(responseBody);
                return user;
            });
        }

        /// <summary>
        /// 指定したIDでユーザ情報を取得します。
        /// </summary>
        public User GetUserById(Int64 id)
        {
            return ExecuteRequest<User>(() =>
            {
                String responseBody = GET(String.Format("/users/show.json?id={0}&include_entities=true", id));
                User user = JsonConvert.DeserializeObject<User>(responseBody);
                return user;
            });
        }

        /// <summary>
        /// timeline を取得します。
        /// </summary>
        public List<Tweet> GetTimeline(Int64 sinceId)
        {
            return GetTimeline(sinceId, FetchCount);
        }

        /// <summary>
        /// timeline を取得します。
        /// </summary>
        public List<Tweet> GetTimeline(Int64 sinceId, Int32 count)
        {
            return ExecuteRequest<List<Tweet>>(() =>
            {
                String responseBody = GET(String.Format("/statuses/home_timeline.json?since_id={0}&count={1}&include_entities=true", sinceId, count));
                List<Tweet> tweets = JsonConvert.DeserializeObject<List<Tweet>>(responseBody);
                return tweets;
            });
        }

        /// <summary>
        /// 指定したIDでステータスを取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Tweet GetStatusById(Int64 id)
        {
            return ExecuteRequest<Tweet>(() =>
            {
                String responseBody = GET(String.Format("/statuses/show.json?id={0}&include_entities=true", id));
                Tweet tweet = JsonConvert.DeserializeObject<Tweet>(responseBody);
                return tweet;
            });
        }

        /// <summary>
        /// replies を取得します。
        /// </summary>
        [Obsolete]
        public List<Tweet> GetReplies()
        {
            return GetMentions();
        }

        /// <summary>
        /// mentions を取得します。
        /// </summary>
        public List<Tweet> GetMentions()
        {
            return GetMentions(1);
        }

        /// <summary>
        /// mentions を取得します。
        /// </summary>
        public List<Tweet> GetMentions(Int64 sinceId)
        {
            return ExecuteRequest<List<Tweet>>(() =>
            {
                String responseBody = GET(String.Format("/statuses/mentions_timeline.json?since_id={0}&include_entities=true", sinceId));
                List<Tweet> tweets = JsonConvert.DeserializeObject<List<Tweet>>(responseBody);
                return tweets;
            });
        }

        /// <summary>
        /// direct messages を取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public List<DirectMessage> GetDirectMessages()
        {
            return GetDirectMessages(0);
        }

        /// <summary>
        /// direct messages を取得します。
        /// </summary>
        public List<DirectMessage> GetDirectMessages(Int64 sinceId)
        {
            return ExecuteRequest<List<DirectMessage>>(() =>
            {
                String responseBody = GET(String.Format("/direct_messages.json{0}", (sinceId != 0 ? "?since_id=" + sinceId : "")));
                List<DirectMessage> directMessages = JsonConvert.DeserializeObject<List<DirectMessage>>(responseBody);
                return directMessages;
            });
        }

        /// <summary>
        /// user timeline を取得します。
        /// </summary>
        public List<Tweet> GetTimelineByScreenName(String screenName,Int64 sinceId, Int32 count)
        {
            return ExecuteRequest<List<Tweet>>(() =>
            {
                String responseBody = GET(String.Format("/statuses/user_timeline.json?screen_name={0}{1}{2}", screenName, (sinceId != 0) ? "&since_id=" + sinceId : "", (count > 0 ? "&count="+ count : "")));
                List<Tweet> tweets = JsonConvert.DeserializeObject<List<Tweet>>(responseBody);
                return tweets;
            });
        }

        /// <summary>
        /// 指定したユーザの favorites を取得します。
        /// </summary>
        public List<Tweet> GetFavoritesByScreenName(String screenName, Int64 sinceId)
        {
            return ExecuteRequest<List<Tweet>>(() =>
            {
                String responseBody = GET(String.Format("/favorites/list.json?screen_name={0}{1}", screenName, (sinceId != 0 ? "&since_id=" + sinceId :"")));
                List<Tweet> tweets = JsonConvert.DeserializeObject<List<Tweet>>(responseBody);
                return tweets;
            });
        }

        /// <summary>
        /// メッセージをfavoritesに追加します。
        /// </summary>
        public Tweet CreateFavorite(Int64 id)
        {
            return ExecuteRequest<Tweet>(() =>
            {
                String responseBody = POST(String.Format("/favorites/create.json?id={0}", id), "");
                Tweet tweet = JsonConvert.DeserializeObject<Tweet>(responseBody);
                return tweet;
            });
        }

        /// <summary>
        /// メッセージをfavoritesから削除します。
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Tweet DestroyFavorite(Int64 id)
        {
            return ExecuteRequest<Tweet>(() =>
            {
                String responseBody = POST(String.Format("/favorites/destroy.json?id={0}", id), "");
                Tweet tweet = JsonConvert.DeserializeObject<Tweet>(responseBody);
                return tweet;
            });
        }

        /// <summary>
        /// メッセージを削除します。
        /// </summary>
        public Tweet DestroyStatus(Int64 id)
        {
            return ExecuteRequest<Tweet>(() =>
            {
                String responseBody = POST(String.Format("/statuses/destroy/{0}.json", id), "");
                Tweet tweet = JsonConvert.DeserializeObject<Tweet>(responseBody);
                return tweet;
            });
        }

        /// <summary>
        /// メッセージをretweetします。
        /// </summary>
        public Tweet RetweetStatus(Int64 id)
        {
            return ExecuteRequest<Tweet>(() =>
            {
                String responseBody = POST(String.Format("/statuses/retweet/{0}.json", id), "");
                Tweet tweet = JsonConvert.DeserializeObject<Tweet>(responseBody);
                return tweet;
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
                String responseBody = POST("/friendships/create.json", postData);
                User user = JsonConvert.DeserializeObject<User>(responseBody);
                return user;
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
                String responseBody = POST("/friendships/destroy.json", postData);
                User user = JsonConvert.DeserializeObject<User>(responseBody);
                return user;
            });
        }

        /// <summary>
        /// ユーザをblockします。
        /// </summary>
        public User CreateBlock(String screenName)
        {
            return ExecuteRequest<User>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/blocks/create.json", postData);
                User user = JsonConvert.DeserializeObject<User>(responseBody);
                return user;
            });
        }

        /// <summary>
        /// ユーザへのblockを解除します。
        /// </summary>
        public User DestroyBlock(String screenName)
        {
            return ExecuteRequest<User>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/blocks/destroy.json", postData);
                User user = JsonConvert.DeserializeObject<User>(responseBody);
                return user;
            });
        }

        /// <summary>
        /// ユーザをspam報告します。
        /// </summary>
        public User ReportSpam(String screenName)
        {
            return ExecuteRequest<User>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/users/report_spam.json", postData);
                User user = JsonConvert.DeserializeObject<User>(responseBody);
                return user;
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
        private void UpdateLastAccessTweetId(Tweet tweet, ref Int64 sinceId)
        {
            if (ProcessDropProtection(_statusBuffer, tweet.Id))
            {
                if (EnableDropProtection)
                {
                    // 取りこぼし防止しているときは一番古いID
                    if (tweet.Id < sinceId)
                    {
                        sinceId = tweet.Id;
                    }
                }
                else
                {
                    if (tweet.Id > sinceId)
                    {
                        sinceId = tweet.Id;
                    }
                }
            }
        }
        
        /// <summary>
        /// ステータスがすでに流されたかどうかをチェックして、流されていない場合に指定されたアクションを実行します。
        /// </summary>
        public void ProcessTweet(Tweet tweet, Action<Tweet> action)
        {
            if (ProcessDropProtection(_statusBuffer, tweet.Id))
            {
                action(tweet);
                UpdateLastAccessTweetId(tweet, ref _lastAccessTimelineId);
            }
         }

        /// <summary>
        /// ステータスがすでに流されたかどうかをチェックして、流されていない場合に指定されたアクションを実行します。
        /// </summary>
        public void ProcessTweets(List<Tweet> tweets, Action<List<Tweet>> action)
        {
            List<Tweet> tmpTweets = new List<Tweet>();
            List<Tweet> tweetList = new List<Tweet>();
            foreach (Tweet tweet in tweets)
            {
                ProcessTweet(tweet, s =>
                {
                    tweetList.Add(tweet);
                    UpdateLastAccessTweetId(tweet, ref _lastAccessTimelineId);
                });
            }

            if (tweetList.Count == 0)
                return;
            tmpTweets = tweetList;
            action(tmpTweets);
        }
        /// <summary>
        /// Repliesステータスがすでに流されたかどうかをチェックして、流されていない場合に指定されたアクションを実行します。
        /// </summary>
        public void ProcessRepliesTweet(List<Tweet> tweets, Action<List<Tweet>> action)
        {
            List<Tweet> tmpTweets = new List<Tweet>();
            List<Tweet> tweetList = new List<Tweet>();
            foreach (Tweet tweet in tweets)
            {
                ProcessTweet(tweet, s =>
                {
                    tweetList.Add(tweet);
                    UpdateLastAccessTweetId(tweet, ref _lastAccessTimelineId);
                });
            }
            foreach (Tweet tweet in tweets.Where(s => s.Id > _lastAccessRepliesId))
            {
                if (ProcessDropProtection(_repliesBuffer, tweet.Id) && ProcessDropProtection(_statusBuffer, tweet.Id))
                {
                    tweetList.Add(tweet);
                }
                UpdateLastAccessTweetId(tweet, ref _lastAccessTimelineId);
                UpdateLastAccessTweetId(tweet, ref _lastAccessRepliesId);
            }

            if (tweetList.Count == 0)
                return;
            tmpTweets = tweetList;
            action(tmpTweets);
        }

        private void CheckNewTimeLine()
        {
            Boolean friendsCheckRequired = false;
            RunCheck(delegate
            {
                List<Tweet> tweets = GetTimeline(_lastAccessTimelineId);
                //Array.Reverse(tweets.Status);
                tweets.Reverse();
                // 差分チェック
                ProcessTweets(tweets, (s) =>
                {
                    OnTimelineStatusesReceived(new StatusesUpdatedEventArgs(s, _isFirstTime, friendsCheckRequired));
                });

                if (_isFirstTime || !EnableDropProtection)
                {
                    if (tweets != null && tweets.Count > 0)
                        _lastAccessTimelineId = tweets.Select(s => s.Id).Max();
                }
                _isFirstTime = false;
            });
        }

        private void CheckDirectMessage()
        {
            RunCheck(delegate
            {
                List<DirectMessage> directMessages = (_lastAccessDirectMessageId == 0) ? GetDirectMessages() : GetDirectMessages(_lastAccessDirectMessageId);
                directMessages.Reverse();
                foreach (DirectMessage message in directMessages)
                {
                    // チェック
                    if (message == null || String.IsNullOrEmpty(message.Sender.ScreenName))
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
                List<Tweet> tweets = GetMentions(_lastAccessRepliesId);
                tweets.Reverse();

                // 差分チェック
                ProcessRepliesTweet(tweets, (s) =>
                {
                    // Here I pass dummy, because no matter how the replier flags
                    // friendsCheckRequired, we cannot receive his or her info
                    // through get_friends.
                    OnRepliesReceived(new StatusesUpdatedEventArgs(s, _isFirstTimeReplies, friendsCheckRequired));
                });

                if (_isFirstTimeReplies || !EnableDropProtection)
                {
                    if (tweets != null && tweets.Count > 0)
                        _lastAccessRepliesId = tweets.Select(s => s.Id).Max();
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
                throw new TwitterServiceException(ioe);
            }
            catch (JsonException je)
            {
                throw new TwitterServiceException(je);
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
                throw new TwitterServiceException(ioe);
            }
            catch (JsonException je)
            {
                throw new TwitterServiceException(je);
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
        /// 指定されたURLからデータを取得し文字列として返します。
        /// </summary>
        /// <param name="url">データを取得するURL</param>
        /// <returns></returns>
        public String GET(String url)
        {
            TraceLogger.Twitter.Information("GET: " + url);
            return GETWithOAuth(url);
        }

        public String POST(String url, String postData)
        {
            TraceLogger.Twitter.Information("POST: " + url);
            return OAuthClient.Request(new Uri(ServiceServerPrefix + url), TwitterOAuth.HttpMethod.POST, postData);
        }

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
        public List<Tweet> Tweets { get; set; }
        public Boolean IsFirstTime { get; set; }
        public Boolean FriendsCheckRequired { get; set; }
        public StatusesUpdatedEventArgs(List<Tweet> tweets)
        {
            this.Tweets = tweets;
        }
        public StatusesUpdatedEventArgs(List<Tweet> tweets, Boolean isFirstTime, Boolean friendsCheckRequired)
            : this(tweets)
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
    /// 
    /// </summary>
    public class User
    {
        // https://dev.twitter.com/docs/platform-objects/users
        [JsonProperty("contributors_enabled", NullValueHandling = NullValueHandling.Ignore)]
        public bool ContributorsEnabled { get; set; }
        [JsonProperty("created_at", NullValueHandling = NullValueHandling.Ignore)]
        public string _createdAt { get; set; }
        [JsonIgnore]
        public DateTime CreatedAt
        {
            get
            {
                return Utility.ParseDateTime(_createdAt);
            }
        }
        [JsonProperty("default_profile", NullValueHandling = NullValueHandling.Ignore)]
        public bool DefaultProfile { get; set; }
        [JsonProperty("default_profile_image", NullValueHandling = NullValueHandling.Ignore)]
        public bool DefaultProfileImage { get; set; }
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }
        [JsonProperty("entities", NullValueHandling = NullValueHandling.Ignore)]
        public Entity Entities { get; set; }
        [JsonProperty("favourites_count", NullValueHandling = NullValueHandling.Ignore)]
        public int FavouritesCount { get; set; }
        [JsonProperty("follow_request_sent", NullValueHandling = NullValueHandling.Ignore)]
        public bool FollowRequestSent { get; set; }
        [JsonProperty("following", NullValueHandling = NullValueHandling.Ignore)]
        public bool Following { get; set; }
        [JsonProperty("followers_count", NullValueHandling = NullValueHandling.Ignore)]
        public int FollowersCount { get; set; }
        [JsonProperty("friends_count", NullValueHandling = NullValueHandling.Ignore)]
        public int FriendsCount { get; set; }
        [JsonProperty("geo_enabled", NullValueHandling = NullValueHandling.Ignore)]
        public bool GeoEnabled { get; set; }
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long Id { get; set; }
        [JsonProperty("id_str", NullValueHandling = NullValueHandling.Ignore)]
        public string IdStr { get; set; }
        [JsonProperty("is_translator", NullValueHandling = NullValueHandling.Ignore)]
        public bool IsTranslator { get; set; }
        [JsonProperty("lang", NullValueHandling = NullValueHandling.Ignore)]
        public string Lang { get; set; }
        [JsonProperty("listed_count", NullValueHandling = NullValueHandling.Ignore)]
        public int ListedCount { get; set; }
        [JsonProperty("location")]
        public string Location { get; set; }
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
        [JsonProperty("notifications", NullValueHandling = NullValueHandling.Ignore)]
        public bool Notifications { get; set; }
        [JsonProperty("profile_background_color", NullValueHandling = NullValueHandling.Ignore)]
        public string ProfileBackgroundColor { get; set; }
        [JsonProperty("profile_background_image_url", NullValueHandling = NullValueHandling.Ignore)]
        public string ProfileBackgroundImage_url { get; set; }
        [JsonProperty("profile_background_image_url_https", NullValueHandling = NullValueHandling.Ignore)]
        public string ProfileBackgroundImageUrlHttps { get; set; }
        [JsonProperty("profile_background_tile", NullValueHandling = NullValueHandling.Ignore)]
        public bool ProfileBackgroundTile { get; set; }
        [JsonProperty("profile_banner_url", NullValueHandling = NullValueHandling.Ignore)]
        public string ProfileBannerUrl { get; set; }
        [JsonProperty("profile_image_url", NullValueHandling = NullValueHandling.Ignore)]
        public string ProfileImageUrl { get; set; }
        [JsonProperty("profile_image_url_https", NullValueHandling = NullValueHandling.Ignore)]
        public string ProfileImageUrlHttps { get; set; }
        [JsonProperty("profile_link_color", NullValueHandling = NullValueHandling.Ignore)]
        public string ProfileLinkColor { get; set; }
        [JsonProperty("profile_sidebar_border_color", NullValueHandling = NullValueHandling.Ignore)]
        public string ProfileSidebarBorderColor { get; set; }
        [JsonProperty("profile_sidebar_fill_color", NullValueHandling = NullValueHandling.Ignore)]
        public string ProfileSidebarFillColor { get; set; }
        [JsonProperty("profile_text_color", NullValueHandling = NullValueHandling.Ignore)]
        public string ProfileTextColor { get; set; }
        [JsonProperty("profile_use_background_image", NullValueHandling = NullValueHandling.Ignore)]
        public bool ProfileUseBackgroundImage { get; set; }
        [JsonProperty("protected", NullValueHandling = NullValueHandling.Ignore)]
        public bool Protected { get; set; }
        [JsonProperty("screen_name", NullValueHandling = NullValueHandling.Ignore)]
        public string ScreenName { get; set; }
        [JsonProperty("show_all_inline_media", NullValueHandling = NullValueHandling.Ignore)]
        public bool ShowAllInlineMedia { get; set; }
        [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
        public Tweet Status { get; set; }
        [JsonProperty("statuses_count", NullValueHandling = NullValueHandling.Ignore)]
        public int StatusesCount { get; set; }
        [JsonProperty("time_zone", NullValueHandling = NullValueHandling.Ignore)]
        public string TimeZone { get; set; }
        [JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
        public string Url { get; set; }
        [JsonProperty("utc_offset", NullValueHandling = NullValueHandling.Ignore)]
        public int UtcOffset { get; set; }
        [JsonProperty("verified", NullValueHandling = NullValueHandling.Ignore)]
        public bool Verified { get; set; }
        [JsonProperty("withheld_in_countries", NullValueHandling = NullValueHandling.Ignore)]
        public string WithheldInCountries { get; set; }
        [JsonProperty("withheld_scope", NullValueHandling = NullValueHandling.Ignore)]
        public string WithheldScope { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class UserIds
    {
        // https://dev.twitter.com/docs/api/1.1/get/friends/ids
        // https://dev.twitter.com/docs/api/1.1/get/followers/ids

        [JsonProperty("ids", NullValueHandling = NullValueHandling.Ignore)]
        public List<long> Ids { get; set; }
        [JsonProperty("next_cursor", NullValueHandling = NullValueHandling.Ignore)]
        public long NextCursor { get; set; }
        [JsonProperty("next_cursor_str", NullValueHandling = NullValueHandling.Ignore)]
        public string NextCursor_str { get; set; }
        [JsonProperty("previous_cursor", NullValueHandling = NullValueHandling.Ignore)]
        public long PreviousCursor { get; set; }
        [JsonProperty("previous_cursor_str", NullValueHandling = NullValueHandling.Ignore)]
        public string PreviousCursorStr { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Tweet
    {
        // https://dev.twitter.com/docs/platform-objects/tweets
        [JsonProperty("contributors", NullValueHandling = NullValueHandling.Ignore)]
        public Object Contributors { get; set; }
        [JsonProperty("coordinates", NullValueHandling = NullValueHandling.Ignore)]
        public Object Coordinates { get; set; }
        [JsonProperty("created_at", NullValueHandling = NullValueHandling.Ignore)]
        public string _createdAt { get; set; }
        [JsonIgnore]
        private DateTime __createdAt;
        [JsonIgnore]
        public DateTime CreatedAt
        {
            get
            {

                if (!String.IsNullOrEmpty(_createdAt) && __createdAt == DateTime.MinValue) 
                {
                    __createdAt = Utility.ParseDateTime(_createdAt);
                }
                return __createdAt;
            }
            set
            {
                __createdAt = value;
            }
        }
        [JsonProperty("current_user_retweet", NullValueHandling = NullValueHandling.Ignore)]
        public object CurrentUserRetweet { get; set; }
        [JsonProperty("entities", NullValueHandling = NullValueHandling.Ignore)]
        public Entity Entities { get; set; }
        [JsonProperty("favorited", NullValueHandling = NullValueHandling.Ignore)]
        public bool Favorited { get; set; }
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long Id { get; set; }
        [JsonProperty("id_str", NullValueHandling = NullValueHandling.Ignore)]
        public string IdStr { get; set; }
        [JsonProperty("in_reply_to_screen_name", NullValueHandling = NullValueHandling.Ignore)]
        public string InReplyToScreenName { get; set; }
        [JsonProperty("in_reply_to_status_id", NullValueHandling = NullValueHandling.Ignore)]
        public long InReplyToStatusId { get; set; }
        [JsonProperty("in_reply_to_status_id_str", NullValueHandling = NullValueHandling.Ignore)]
        public string InReplyToStatusIdStr { get; set; }
        [JsonProperty("in_reply_to_user_id", NullValueHandling = NullValueHandling.Ignore)]
        public long InReplyToUserId { get; set; }
        [JsonProperty("in_reply_to_user_id_str", NullValueHandling = NullValueHandling.Ignore)]
        public string InReplyToUserIdStr { get; set; }
        [JsonProperty("place", NullValueHandling = NullValueHandling.Ignore)]
        public Place Place { get; set; }
        [JsonProperty("possibly_sensitive", NullValueHandling = NullValueHandling.Ignore)]
        public bool PossiblySensitive { get; set; }
        [JsonProperty("scopes", NullValueHandling = NullValueHandling.Ignore)]
        public object Scopes { get; set; }
        [JsonProperty("retweet_count", NullValueHandling = NullValueHandling.Ignore)]
        public int RetweetCount { get; set; }
        [JsonProperty("retweeted", NullValueHandling = NullValueHandling.Ignore)]
        public bool Retweeted { get; set; }
        [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)]
        public string Source { get; set; }
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string _text { get; set; }
        [JsonIgnore]
        public string Text
        {
            get
            {
                if (!String.IsNullOrEmpty(_text) && _text == null)
                {
                    _text = Utility.UnescapeCharReference(_text);
                }

                return _text ?? "";
            }
            set
            {
                _text = value;
            }
        }
        [JsonProperty("truncated", NullValueHandling = NullValueHandling.Ignore)]
        public bool Truncated { get; set; }
        [JsonProperty("user", NullValueHandling = NullValueHandling.Ignore)]
        public User User { get; set; }
        [JsonProperty("withheld_copyright", NullValueHandling = NullValueHandling.Ignore)]
        public bool WithheldCopyright { get; set; }
        [JsonProperty("withheld_in_countries", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> WithheldInCountries { get; set; }
        [JsonProperty("withheld_scope", NullValueHandling = NullValueHandling.Ignore)]
        public string WithheldScope { get; set; }
        [JsonProperty("retweeted_status", NullValueHandling = NullValueHandling.Ignore)]
        public Tweet RetweetedStatus { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Entity
    {
        [JsonProperty("hashtags", NullValueHandling = NullValueHandling.Ignore)]
        public List<EntitiesHashtag> Hashtags { get; set; }
        [JsonProperty("media")]
        public List<EntitiesMedia> Media { get; set; }
        [JsonProperty("urls", NullValueHandling = NullValueHandling.Ignore)]
        public List<EntitiesUrl> Urls { get; set; }
        [JsonProperty("user_mentions", NullValueHandling = NullValueHandling.Ignore)]
        public List<EntitiesUserMention> UserMentions { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Coordinate
    {
        // https://dev.twitter.com/docs/platform-objects/tweets#obj-coordinates
        [JsonProperty("coordinates", NullValueHandling = NullValueHandling.Ignore)]
        public List<float> Coordinates { get; set; }
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string Type { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Contributor
    {
        // https://dev.twitter.com/docs/platform-objects/tweets#obj-contributors
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long Id { get; set; }
        [JsonProperty("id_str", NullValueHandling = NullValueHandling.Ignore)]
        public string IdStr { get; set; }
        [JsonProperty("screen_name", NullValueHandling = NullValueHandling.Ignore)]
        public string ScreenName { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Place
    {
        // https://dev.twitter.com/docs/platform-objects/places
        [JsonProperty("attributes", NullValueHandling = NullValueHandling.Ignore)]
        public object Attributes { get; set; }
        [JsonProperty("bounding_box", NullValueHandling = NullValueHandling.Ignore)]
        public PlacesBoundingBox BoundingBox { get; set; }
        [JsonProperty("country", NullValueHandling = NullValueHandling.Ignore)]
        public string Country { get; set; }
        [JsonProperty("country_code", NullValueHandling = NullValueHandling.Ignore)]
        public string CountryCode { get; set; }
        [JsonProperty("full_name", NullValueHandling = NullValueHandling.Ignore)]
        public string FullName { get; set; }
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
        [JsonProperty("place_type", NullValueHandling = NullValueHandling.Ignore)]
        public string PlaceType { get; set; }
        [JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
        public string Url { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class PlacesBoundingBox
    {
        // https://dev.twitter.com/docs/platform-objects/places#obj-boundingbox
        [JsonProperty("coordinates", NullValueHandling = NullValueHandling.Ignore)]
        public List<List<List<float>>> Coordinates { get; set; }
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string Type { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesHashtag
    {
        [JsonProperty("indices", NullValueHandling = NullValueHandling.Ignore)]
        public List<int> Indices { get; set; }
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesMedia
    {
        [JsonProperty("display_url", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayUrl { get; set; }
        [JsonProperty("expanded_url", NullValueHandling = NullValueHandling.Ignore)]
        public string ExpandedUrl { get; set; }
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long Id { get; set; }
        [JsonProperty("id_str", NullValueHandling = NullValueHandling.Ignore)]
        public string IdStr { get; set; }
        [JsonProperty("indices", NullValueHandling = NullValueHandling.Ignore)]
        public List<int> Indices { get; set; }
        [JsonProperty("media_url", NullValueHandling = NullValueHandling.Ignore)]
        public string MediaUrl { get; set; }
        [JsonProperty("media_url_https", NullValueHandling = NullValueHandling.Ignore)]
        public string MediaUrlHttps { get; set; }
        [JsonProperty("sizes", NullValueHandling = NullValueHandling.Ignore)]
        public EntitiesSizes Sizes { get; set; }
        [JsonProperty("source_status_id", NullValueHandling = NullValueHandling.Ignore)]
        public long SourceStatusId { get; set; }
        [JsonProperty("source_status_id_str", NullValueHandling = NullValueHandling.Ignore)]
        public string SourceStatusIdStr { get; set; }
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string Type { get; set; }
        [JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
        public string Url { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesSize
    {
        [JsonProperty("h", NullValueHandling = NullValueHandling.Ignore)]
        public int H { get; set; }
        [JsonProperty("resize", NullValueHandling = NullValueHandling.Ignore)]
        public string Resize { get; set; }
        [JsonProperty("w", NullValueHandling = NullValueHandling.Ignore)]
        public int W { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesSizes
    {
        [JsonProperty("thumb", NullValueHandling = NullValueHandling.Ignore)]
        public EntitiesSize Thumb { get; set; }
        [JsonProperty("large", NullValueHandling = NullValueHandling.Ignore)]
        public EntitiesSize Large { get; set; }
        [JsonProperty("medium", NullValueHandling = NullValueHandling.Ignore)]
        public EntitiesSize Medium { get; set; }
        [JsonProperty("small", NullValueHandling = NullValueHandling.Ignore)]
        public EntitiesSize Small { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesUrl
    {
        [JsonProperty("display_url", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayUrl { get; set; }
        [JsonProperty("expanded_url", NullValueHandling = NullValueHandling.Ignore)]
        public string ExpandedUrl { get; set; }
        [JsonProperty("indices", NullValueHandling = NullValueHandling.Ignore)]
        public List<int> Indices { get; set; }
        [JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
        public string Url { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesUserMention
    {
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long Id { get; set; }
        [JsonProperty("id_str", NullValueHandling = NullValueHandling.Ignore)]
        public string IdStr { get; set; }
        [JsonProperty("indices", NullValueHandling = NullValueHandling.Ignore)]
        public List<int> Indices { get; set; }
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
        [JsonProperty("screen_name", NullValueHandling = NullValueHandling.Ignore)]
        public string ScreenName { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class DirectMessage
    {
        [JsonProperty("created_at", NullValueHandling = NullValueHandling.Ignore)]
        public string _createdAt { get; set; }
        [JsonIgnore]
        public DateTime CreatedAt
        {
            get
            {
                return Utility.ParseDateTime(_createdAt);
            }
        }
        [JsonProperty("entities", NullValueHandling = NullValueHandling.Ignore)]
        public Entity Entities { get; set; }
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long Id { get; set; }
        [JsonProperty("id_str", NullValueHandling = NullValueHandling.Ignore)]
        public string IdStr { get; set; }
        [JsonProperty("recipient", NullValueHandling = NullValueHandling.Ignore)]
        public User Recipient { get; set; }
        [JsonProperty("recipient_id", NullValueHandling = NullValueHandling.Ignore)]
        public long RecipientId { get; set; }
        [JsonProperty("recipient_screen_name", NullValueHandling = NullValueHandling.Ignore)]
        public string RecipientScreenName { get; set; }
        [JsonProperty("sender", NullValueHandling = NullValueHandling.Ignore)]
        public User Sender { get; set; }
        [JsonProperty("sender_id", NullValueHandling = NullValueHandling.Ignore)]
        public long SenderId { get; set; }
        [JsonProperty("sender_screen_name", NullValueHandling = NullValueHandling.Ignore)]
        public string SenderScreeenName { get; set; }
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }
    }
}
