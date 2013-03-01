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
        public Users VerifyCredential()
        {
            return ExecuteRequest<Users>(() =>
            {
                // TODO: デシリアライズ処理をここへ
                return new Users();
            });
        }

        /// <summary>
        /// ステータスを更新します。
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public Tweets UpdateStatus(String message)
        {
            return UpdateStatus(message, 0);
        }

        /// <summary>
        /// ステータスを更新します。
        /// </summary>
        /// <param name="message"></param>
        /// <param name="inReplyToStatusId"></param>
        public Tweets UpdateStatus(String message, Int64 inReplyToStatusId)
        {
            String encodedMessage = TwitterService.EncodeMessage(message);
            return ExecuteRequest<Tweets>(() =>
            {
                String postData = String.Format("status={0}{1}", encodedMessage, (inReplyToStatusId != 0 ? "&in_reply_to_status_id=" + inReplyToStatusId : ""));
                String responseBody = POST("/statuses/update.json", postData);
                // TODO: デシリアライズ処理をここへ
                return new Tweets();
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
                String responseBody = POST("/direct_messages/new.json", postData);
            });
        }

        /// <summary>
        /// friendsを取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Users[] GetFriends()
        {
            return GetFriends(1);
        }

        /// <summary>
        /// friendsを取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Users[] GetFriends(Int32 maxPage)
        {
            // FIXME: http://pplace.jp/2012/10/1058/
            List<Users> users = new List<Users>();
            Int64 cursor = -1;
            Int32 page = maxPage;
            return ExecuteRequest<Users[]>(() =>
            {
                while (cursor != 0 && page > 0)
                {
                    String responseBody = GET(String.Format("/statuses/friends.xml?cursor={0}&lite=true", cursor));
                    // TODO: デシリアライズ処理をここへ
                    return new Users[]();
                }

                return users.ToArray();
            });
        }

        /// <summary>
        /// userを取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Users GetUser(String screenName)
        {
            return ExecuteRequest<Users>(() =>
            {
                String responseBody = GET(String.Format("/users/show.json?screen_name={0}&include_entities=true", screenName), false);
                // TODO: デシリアライズ処理をここへ
                return new Users();
            });
        }
        /// <summary>
        /// 指定したIDでユーザ情報を取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Users GetUserById(Int32 id)
        {
            return ExecuteRequest<Users>(() =>
            {
                String responseBody = GET(String.Format("/users/show.json?id={0}&include_entities=true", id), false);
                // TODO: デシリアライズ処理をここへ
                return new Users();
            });
        }

        /// <summary>
        /// timeline を取得します。
        /// </summary>
        /// <param name="sinceId">最後に取得したID</param>
        /// <returns>ステータス</returns>
        public Tweets[] GetTimeline(Int64 sinceId)
        {
            // TODO: この関数で呼ばれているかのチェック
            return GetTimeline(sinceId, FetchCount);
        }

        /// <summary>
        /// timeline を取得します。
        /// </summary>
        /// <param name="sinceId">最後に取得したID</param>
        /// <param name="count">取得数</param>
        /// <returns>ステータス</returns>
        public Tweets[] GetTimeline(Int64 sinceId, Int32 count)
        {
            return ExecuteRequest<Tweets[]>(() =>
            {
                String responseBody = GET(String.Format("/statuses/home_timeline.json?since_id={0}&count={1}&include_entities=true", sinceId, count));
                // TODO: デシリアライズ処理をここへ
                return new Tweets[]();
            });
        }

        /// <summary>
        /// 指定したIDでステータスを取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Tweets GetStatusById(Int64 id)
        {
            return ExecuteRequest<Tweets>(() =>
            {
                String responseBody = GET(String.Format("/statuses/show.json?id={0}&include_entities=true", id), false);
                // TODO: デシリアライズ処理をここへ
                return new Tweets();
            });
        }

        /// <summary>
        /// replies を取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        [Obsolete]
        public Tweets[] GetReplies()
        {
            return GetMentions();
        }

        /// <summary>
        /// mentions を取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Tweets[] GetMentions()
        {
            return GetMentions(1);
        }

        /// <summary>
        /// mentions を取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Tweets[] GetMentions(Int64 sinceId)
        {
            return ExecuteRequest<Tweets[]>(() =>
            {
                String responseBody = GET(String.Format("/statuses/mentions_timeline.json?since_id={0}&include_entities=true", sinceId));
                // TODO: デシリアライズ処理をここへ
                return new Tweets[]();
            });
        }

        /// <summary>
        /// direct messages を取得します。
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public DirectMessages[] GetDirectMessages()
        {
            return GetDirectMessages(0);
        }

        /// <summary>
        /// direct messages を取得します。
        /// </summary>
        /// <param name="sinceId">最後に取得したID</param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public DirectMessage[] GetDirectMessages(Int64 sinceId)
        {
            // TODO: この関数が使われているかのチェック
            return ExecuteRequest<DirectMessage[]>(() =>
            {
                // Cookie ではダメ
                String responseBody = GET(String.Format("/direct_messages.json{0}", (sinceId != 0 ? "?since_id=" + sinceId : "")), false);
                // TODO: デシリアライズ処理をここへ
                return new DirectMessage[]();
            });
        }

        /// <param name="screenName">スクリーンネーム</param>
        /// <param name="since">最終更新日時</param>
        /// <param name="count"></param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Tweets[] GetTimelineByScreenName(String screenName, DateTime since, Int32 count)
        {
            /*
             * FIXME: since_idでの対応
             * または最新のツイートのみ取得するためにsince_idを使わない仕様に変更
             * https://dev.twitter.com/docs/api/1.1/get/statuses/user_timeline
             */
            return ExecuteRequest<Tweets[]>(() =>
            {
                StringBuilder sb = new StringBuilder();
                if (since != new DateTime())
                    sb.Append("since=").Append(Utility.UrlEncode(since.ToUniversalTime().ToString("r"))).Append("&");
                if (count > 0)
                    sb.Append("count=").Append(count).Append("&");

                String responseBody = GET(String.Format("/statuses/user_timeline.json?screen_name={0}&{1}", screenName, sb.ToString()));
                // TODO: デシリアライズ処理をここへ
                return new Tweets[]();
            });
        }

        /// <summary>
        /// 指定したユーザの favorites を取得します。
        /// </summary>
        /// <param name="screenName">スクリーンネーム</param>
        /// <param name="page">ページ</param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Tweets[] GetFavoritesByScreenName(String screenName, Int32 page)
        {
            /*
             * FIXME:
             * pageからsince_idへ変更
             * https://dev.twitter.com/docs/api/1.1/get/favorites/list
             */
            return ExecuteRequest<Tweets[]>(() =>
            {
                StringBuilder sb = new StringBuilder();
                if (page > 0)
                    sb.Append("page=").Append(page).Append("&");

                String responseBody = GET(String.Format("/favorites/list.json?screen_name={0}&{1}", screenName, sb.ToString()));
                // TODO: デシリアライズ処理をここへ
                return new Tweets[]();
            });
        }

        /// <summary>
        /// メッセージをfavoritesに追加します。
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Tweets CreateFavorite(Int64 id)
        {
            return ExecuteRequest<Tweets>(() =>
            {
                String responseBody = POST(String.Format("/favorites/create.json?id={0}", id), "");
                // TODO: デシリアライズ処理をここへ
                return new Tweets();
            });
        }

        /// <summary>
        /// メッセージをfavoritesから削除します。
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Tweets DestroyFavorite(Int64 id)
        {
            return ExecuteRequest<Status>(() =>
            {
                String responseBody = POST(String.Format("/favorites/destroy.json?id={0}", id), "");
                // TODO: デシリアライズ処理をここへ
                return new Tweets();
            });
        }

        /// <summary>
        /// メッセージを削除します。
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Tweets DestroyStatus(Int64 id)
        {
            return ExecuteRequest<Status>(() =>
            {
                String responseBody = POST(String.Format("/statuses/destroy/{0}.json", id), "");
                // TODO: デシリアライズ処理をここへ
                return new Tweets();
            });
        }

        /// <summary>
        /// メッセージをretweetします。
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Tweets RetweetStatus(Int64 id)
        {
            return ExecuteRequest<Tweets>(() =>
            {
                String responseBody = POST(String.Format("/statuses/retweet/{0}.json", id), "");
                // TODO: デシリアライズ処理をここへ
                return new Tweets();
            });
        }

        /// <summary>
        /// ユーザをfollowします。
        /// </summary>
        /// <param name="screenName"></param>
        /// <returns></returns>
        public Users CreateFriendship(String screenName)
        {
            return ExecuteRequest<Users>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/friendships/create.json", postData);
                // TODO: デシリアライズ処理をここへ
                return new Users();
            });
        }

        /// <summary>
        /// ユーザをremoveします。
        /// </summary>
        /// <param name="screenName"></param>
        /// <returns></returns>
        public Users DestroyFriendship(String screenName)
        {
            return ExecuteRequest<Users>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/friendships/destroy.json", postData);
                // TODO: デシリアライズ処理をここへ
                return new Users();
            });
        }

        /// <summary>
        /// ユーザをblockします。
        /// </summary>
        /// <param name="screenName"></param>
        /// <returns></returns>
        public Users CreateBlock(String screenName)
        {
            return ExecuteRequest<Users>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/blocks/create.json", postData);
                // TODO: デシリアライズ処理をここへ
                return new Users();
            });
        }

        /// <summary>
        /// ユーザへのblockを解除します。
        /// </summary>
        /// <param name="screenName"></param>
        /// <returns></returns>
        public Users DestroyBlock(String screenName)
        {
            return ExecuteRequest<Users>(() =>
            {
                String postData = String.Format("screen_name={0}", screenName);
                String responseBody = POST("/blocks/destroy.json", postData);
                // TODO: デシリアライズ処理をここへ
                return new Users();
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
                if (EnableDropProtection)
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

                if (_isFirstTime || !EnableDropProtection)
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
                DirectMessages directMessages = (_lastAccessDirectMessageId == 0) ? GetDirectMessages() : GetDirectMessages(_lastAccessDirectMessageId);
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

                if (_isFirstTimeReplies || !EnableDropProtection)
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
                // XmlSerializer
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

            HttpWebRequest request = CreateWebRequest("https://twitter.com/account/verify_credentials.json") as HttpWebRequest;
            request.AllowAutoRedirect = false;
            request.Method = "GET";

            NetworkCredential cred = _credential.GetCredential(new Uri("https://twitter.com/account/verify_credentials.json"), "Basic");
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

    /*
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
     */

    /// <summary>
    /// 
    /// </summary>
    public class Users
    {
        // https://dev.twitter.com/docs/platform-objects/users
        [JsonProperty("contributors_enabled")]
        public bool hoge { get; set; }
        [JsonProperty("created_at")]
        public string hoge { get; set; }
        [JsonProperty("default_profile")]
        public bool hoge { get; set; }
        [JsonProperty("default_profile_image")]
        public bool hoge { get; set; }
        [JsonProperty("description")]
        public string hoge { get; set; }
        [JsonProperty("entities")]
        public Entities hoge { get; set; }
        [JsonProperty("favourites_count")]
        public int hoge { get; set; }
        [JsonProperty("follow_request_sent")]
        public bool hoge { get; set; }
        [JsonProperty("following")]
        public bool hoge { get; set; }
        [JsonProperty("followers_count")]
        public int hoge { get; set; }
        [JsonProperty("friends_count")]
        public int hoge { get; set; }
        [JsonProperty("geo_enabled")]
        public bool hoge { get; set; }
        [JsonProperty("id")]
        public long hoge { get; set; }
        [JsonProperty("id_str")]
        public string hoge { get; set; }
        [JsonProperty("is_translator")]
        public bool hoge { get; set; }
        [JsonProperty("lang")]
        public string hoge { get; set; }
        [JsonProperty("listed_count")]
        public int hoge { get; set; }
        [JsonProperty("location")]
        public string hoge { get; set; }
        [JsonProperty("name")]
        public string hoge { get; set; }
        [JsonProperty("notifications")]
        public bool hoge { get; set; }
        [JsonProperty("profile_background_color")]
        public string hoge { get; set; }
        [JsonProperty("profile_background_image_url")]
        public string hoge { get; set; }
        [JsonProperty("profile_background_image_url_https")]
        public string hoge { get; set; }
        [JsonProperty("profile_background_tile")]
        public bool hoge { get; set; }
        [JsonProperty("profile_banner_url")]
        public string hoge { get; set; }
        [JsonProperty("profile_image_url")]
        public string hoge { get; set; }
        [JsonProperty("profile_image_url_https")]
        public string hoge { get; set; }
        [JsonProperty("profile_link_color")]
        public string hoge { get; set; }
        [JsonProperty("profile_sidebar_border_color")]
        public string hoge { get; set; }
        [JsonProperty("profile_sidebar_fill_color")]
        public string hoge { get; set; }
        [JsonProperty("profile_text_color")]
        public string hoge { get; set; }
        [JsonProperty("profile_use_background_image")]
        public bool hoge { get; set; }
        [JsonProperty("protected")]
        public bool hoge { get; set; }
        [JsonProperty("screen_name")]
        public string hoge { get; set; }
        [JsonProperty("show_all_inline_media")]
        public bool hoge { get; set; }
        [JsonProperty("status")]
        public Tweets hoge { get; set; }
        [JsonProperty("statuses_count")]
        public int hoge { get; set; }
        [JsonProperty("time_zone")]
        public string hoge { get; set; }
        [JsonProperty("url")]
        public string hoge { get; set; }
        [JsonProperty("utc_offset")]
        public int hoge { get; set; }
        [JsonProperty("verified")]
        public bool hoge { get; set; }
        [JsonProperty("withheld_in_countries")]
        public string hoge { get; set; }
        [JsonProperty("withheld_scope")]
        public string hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Tweets
    {
        // https://dev.twitter.com/docs/platform-objects/tweets
        [JsonProperty("contributors")]
        public List<Contributors> hoge { get; set; }
        [JsonProperty("coordinates")]
        public List<Coordinates> hoge { get; set; }
        [JsonProperty("created_at")]
        public string hoge { get; set; }
        [JsonProperty("current_user_retweet")]
        public object hoge { get; set; }
        [JsonProperty("entities")]
        public Entities hoge { get; set; }
        [JsonProperty("favorited")]
        public bool hoge { get; set; }
        [JsonProperty("id")]
        public long hoge { get; set; }
        [JsonProperty("id_str")]
        public string hoge { get; set; }
        [JsonProperty("in_reply_to_screen_name")]
        public string hoge { get; set; }
        [JsonProperty("in_reply_to_status_id")]
        public long hoge { get; set; }
        [JsonProperty("in_reply_to_status_id_str")]
        public string hoge { get; set; }
        [JsonProperty("in_reply_to_user_id")]
        public long hoge { get; set; }
        [JsonProperty("in_reply_to_user_id_str")]
        public string hoge { get; set; }
        [JsonProperty("place")]
        public Places hoge { get; set; }
        [JsonProperty("possibly_sensitive")]
        public bool hoge { get; set; }
        [JsonProperty("scopes")]
        public object hoge { get; set; }
        [JsonProperty("retweet_count")]
        public int hoge { get; set; }
        [JsonProperty("retweeted")]
        public bool hoge { get; set; }
        [JsonProperty("source")]
        public string hoge { get; set; }
        [JsonProperty("text")]
        public string hoge { get; set; }
        [JsonProperty("truncated")]
        public bool hoge { get; set; }
        [JsonProperty("user")]
        public Users hoge { get; set; }
        [JsonProperty("withheld_copyright")]
        public bool hoge { get; set; }
        [JsonProperty("withheld_in_countries")]
        public List<string> hoge { get; set; }
        [JsonProperty("withheld_scope")]
        public string hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Entities
    {
        [JsonProperty("hashtags")]
        public List<EntitiesHashtags> hoge { get; set; }
        [JsonProperty("media")]
        public List<EntitiesMedia> hoge { get; set; }
        [JsonProperty("urls")]
        public List<EntitiesUrl> hoge { get; set; }
        [JsonProperty("user_mentions")]
        public List<EntitiesUserMention> hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Coordinates
    {
        // https://dev.twitter.com/docs/platform-objects/tweets#obj-coordinates
        [JsonProperty("coordinates")]
        public List<float> hoge { get; set; }
        [JsonProperty("type")]
        public string hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Contributors
    {
        // https://dev.twitter.com/docs/platform-objects/tweets#obj-contributors
        [JsonProperty("id")]
        public long hoge { get; set; }
        [JsonProperty("id_str")]
        public string hoge { get; set; }
        [JsonProperty("screen_name")]
        public string hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Places
    {
        // https://dev.twitter.com/docs/platform-objects/places
        [JsonProperty("attributes")]
        public object hoge { get; set; }
        [JsonProperty("bounding_box")]
        public PlacesBoundingBox hoge { get; set; }
        [JsonProperty("country")]
        public string hoge { get; set; }
        [JsonProperty("country_code")]
        public string hoge { get; set; }
        [JsonProperty("full_name")]
        public string hoge { get; set; }
        [JsonProperty("id")]
        public string hoge { get; set; }
        [JsonProperty("name")]
        public string hoge { get; set; }
        [JsonProperty("place_type")]
        public string hoge { get; set; }
        [JsonProperty("url")]
        public string hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class PlacesBoundingBox
    {
        // https://dev.twitter.com/docs/platform-objects/places#obj-boundingbox
        [JsonProperty("coordinates")]
        public object hoge { get; set; }
        [JsonProperty("type")]
        public string hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesHashtags
    {
        [JsonProperty("indices")]
        public List<int> hoge { get; set; }
        [JsonProperty("text")]
        public string hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesMedia
    {
        [JsonProperty("display_url")]
        public string hoge { get; set; }
        [JsonProperty("expanded_url")]
        public string hoge { get; set; }
        [JsonProperty("id")]
        public long hoge { get; set; }
        [JsonProperty("id_str")]
        public string hoge { get; set; }
        [JsonProperty("indices")]
        public List<int> hoge { get; set; }
        [JsonProperty("media_url")]
        public string hoge { get; set; }
        [JsonProperty("media_url_https")]
        public string hoge { get; set; }
        [JsonProperty("sizes")]
        public EntitiesSizes hoge { get; set; }
        [JsonProperty("source_status_id")]
        public long hoge { get; set; }
        [JsonProperty("source_status_id_str")]
        public string hoge { get; set; }
        [JsonProperty("type")]
        public string hoge { get; set; }
        [JsonProperty("url")]
        public string hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesSize
    {
        [JsonProperty("h")]
        public int hoge { get; set; }
        [JsonProperty("resize")]
        public string hoge { get; set; }
        [JsonProperty("w")]
        public int hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesSizes
    {
        [JsonProperty("thumb")]
        public EntitiesSize hoge { get; set; }
        [JsonProperty("large")]
        public EntitiesSize hoge { get; set; }
        [JsonProperty("medium")]
        public EntitiesSize hoge { get; set; }
        [JsonProperty("small")]
        public EntitiesSize hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesUrl
    {
        [JsonProperty("display_url")]
        public string hoge { get; set; }
        [JsonProperty("expanded_url")]
        public string hoge { get; set; }
        [JsonProperty("indices")]
        public List<int> hoge { get; set; }
        [JsonProperty("url")]
        public string hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class EntitiesUserMention
    {
        [JsonProperty("id")]
        public long hoge { get; set; }
        [JsonProperty("id_str")]
        public string hoge { get; set; }
        [JsonProperty("indices")]
        public List<int> hoge { get; set; }
        [JsonProperty("name")]
        public string hoge { get; set; }
        [JsonProperty("screen_name")]
        public string hoge { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class DirectMessage
    {
        [JsonProperty("created_at")]
        public string hoge { get; set; }
        [JsonProperty("entities")]
        public Entities hoge { get; set; }
        [JsonProperty("id")]
        public long hoge { get; set; }
        [JsonProperty("id_str")]
        public string hoge { get; set; }
        [JsonProperty("recipient")]
        public Users hoge { get; set; }
        [JsonProperty("recipient_id")]
        public long hoge { get; set; }
        [JsonProperty("recipient_screen_name")]
        public string hoge { get; set; }
        [JsonProperty("sender")]
        public Users hoge { get; set; }
        [JsonProperty("sender_id")]
        public long hoge { get; set; }
        [JsonProperty("sender_screen_name")]
        public string hoge { get; set; }
        [JsonProperty("text")]
        public string hoge { get; set; }
    }

}
