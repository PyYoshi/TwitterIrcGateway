using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Web;
using System.Xml;
using System.Xml.Serialization;
using System.Reflection;
using System.IO;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Globalization;
using System.Threading;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    public class TwitterService : IDisposable
    {
        //private WebClient _webClient;
        private CredentialCache _credential;
        private IWebProxy _proxy = WebRequest.DefaultWebProxy;
        private String _userName;
        private Boolean _cookieLoginMode = false;
        private Boolean _enableDropProtection = true;

        private Timer _timer;
        private Timer _timerDirectMessage;
        private Timer _timerReplies;
        
        private DateTime _lastAccessTimeline = new DateTime();
        private DateTime _lastAccessReplies = new DateTime();
        private DateTime _lastAccessDirectMessage = DateTime.Now;
        private Boolean _isFirstTime = true;
        private Boolean _isFirstTimeReplies = true;

        private Int32 _bufferSize = 250;
        private LinkedList<Status> _statusBuffer;
        private LinkedList<Status> _repliesBuffer;

        public event EventHandler<ErrorEventArgs> CheckError;
        public event EventHandler<StatusesUpdatedEventArgs> TimelineStatusesReceived;
        public event EventHandler<StatusesUpdatedEventArgs> RepliesReceived;
        public event EventHandler<DirectMessageEventArgs> DirectMessageReceived;

        public String ServiceServerPrefix = "http://twitter.com";
        public String Referer = "http://twitter.com/home";
        public String ClientUrl = "http://www.misuzilla.org/dist/net/twitterircgateway/";
        public String ClientVersion = typeof(TwitterService).Assembly.GetName().Version.ToString();
        public String ClientName = "TwitterIrcGateway";

        public TwitterService(String userName, String password)
        {
            CredentialCache credCache = new CredentialCache();
            credCache.Add(new Uri(ServiceServerPrefix), "Basic", new NetworkCredential(userName, password));
            _credential = credCache;

            _userName = userName;
            
            _timer = new Timer(new TimerCallback(OnTimerCallback), null, Timeout.Infinite, Timeout.Infinite);
            _timerDirectMessage = new Timer(new TimerCallback(OnTimerCallbackDirectMessage), null, Timeout.Infinite, Timeout.Infinite);
            _timerReplies = new Timer(new TimerCallback(OnTimerCallbackReplies), null, Timeout.Infinite, Timeout.Infinite);
            
            _statusBuffer = new LinkedList<Status>();
            _repliesBuffer = new LinkedList<Status>();

            //_webClient = new PreAuthenticatedWebClient();
            //_webClient = new WebClient();
            //_webClient.Credentials = _credential;

            Interval = 90;
            IntervalDirectMessage = 360;
            IntervalReplies = 120;
        
            POSTFetchMode = false;
        }

        /// <summary>
        /// �ڑ��ɗ��p����v���L�V��ݒ肵�܂��B
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
        /// Cookie�𗘗p���ă��O�C�����ăf�[�^�ɃA�N�Z�X���܂��B
        /// </summary>
        [Obsolete("Cookie���O�C���ɂ��f�[�^�擾�͐�������܂����BPOSTFetchMode�𗘗p���Ă��������B")]
        public Boolean CookieLoginMode
        {
            get { return _cookieLoginMode; }
            set { _cookieLoginMode = value; }
        }
         
        /// <summary>
        /// POST�𗘗p���ă��O�C�����ăf�[�^�ɃA�N�Z�X���܂��B
        /// </summary>
        public Boolean POSTFetchMode
        {
            get;
            set;
        }
       
        /// <summary>
        /// ��肱�ڂ��h�~��L���ɂ��邩�ǂ������w�肵�܂��B
        /// </summary>
        public Boolean EnableDropProtection
        {
            get { return _enableDropProtection; }
            set { _enableDropProtection = value; }
        }

        /// <summary>
        /// �^�C�����C�����`�F�b�N����Ԋu���w�肵�܂��B
        /// </summary>
        public Int32 Interval
        {
            get;
            set;
        }

        /// <summary>
        /// �_�C���N�g���b�Z�[�W���`�F�b�N����Ԋu���w�肵�܂��B
        /// </summary>
        public Int32 IntervalDirectMessage
        {
            get;
            set;
        }

        /// <summary>
        /// Replies���`�F�b�N����Ԋu���w�肵�܂��B
        /// </summary>
        public Int32 IntervalReplies
        {
            get;
            set;
        }

        /// <summary>
        /// Replies�̃`�F�b�N�����s���邩�ǂ������w�肵�܂��B
        /// </summary>
        public Boolean EnableRepliesCheck
        {
            get;
            set;
        }

        /// <summary>
        /// �X�e�[�^�X���X�V���܂��B
        /// </summary>
        /// <param name="message"></param>
        public Status UpdateStatus(String message)
        {
            String encodedMessage = TwitterService.EncodeMessage(message);
            return ExecuteRequest<Status>(() =>
            {
                String responseBody = POST(String.Format("/statuses/update.xml?status={0}&source={1}", encodedMessage, ClientName), Encoding.Default.GetBytes("1"));
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
        /// �w�肳�ꂽ���[�U�Ƀ_�C���N�g���b�Z�[�W�𑗐M���܂��B
        /// </summary>
        /// <param name="targetId"></param>
        /// <param name="message"></param>
        public void SendDirectMessage(String targetId, String message)
        {
            String encodedMessage = TwitterService.EncodeMessage(message);
            ExecuteRequest(() =>
            {
                String responseBody = POST(String.Format("/direct_messages/new.xml?user={0}&text={1}", targetId, encodedMessage), new Byte[0]);
            });
        }

        /// <summary>
        /// friends���擾���܂��B
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public User[] GetFriends()
        {
            List<User> usersList = new List<User>();
            Int32 page = 0;
            return ExecuteRequest<User[]>(() =>
            {
                while (page++ != 1 /*10*/)
                {
                    String responseBody = GET(String.Format("/statuses/friends.xml?page={0}&lite=true", page));
                    if (NilClasses.CanDeserialize(responseBody))
                    {
                        return usersList.ToArray();
                    }
                    else
                    {
                        Users users = Users.Serializer.Deserialize(new StringReader(responseBody)) as Users;
                        if (users == null || users.User == null)
                        {
                            return usersList.ToArray();
                        }
                        else
                        {
                            usersList.AddRange(users.User);
                        }
                    }
                }
                // ���܂�ɑ����ꍇ�͂����܂ŁB
                return usersList.ToArray();
            });
        }

        /// <summary>
        /// user���擾���܂��B
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public User GetUser(String id)
        {
            return ExecuteRequest<User>(() =>
            {
                String responseBody = GET(String.Format("/users/show/{0}.xml", id), false);
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
        /// timeline ���擾���܂��B
        /// </summary>
        /// <param name="since">�ŏI�X�V����</param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Statuses GetTimeline(DateTime since)
        {
            return ExecuteRequest<Statuses>(() =>
            {
                String responseBody = GET(String.Format("/statuses/friends_timeline.xml?since={0}", Utility.UrlEncode(since.ToUniversalTime().ToString("r"))));
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
        /// replies ���擾���܂��B
        /// </summary>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public Statuses GetReplies()
        {
            return ExecuteRequest<Statuses>(() =>
            {
                String responseBody = GET("/statuses/replies.xml");
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
        /// direct messages ���擾���܂��B
        /// </summary>
        /// <param name="since">�ŏI�X�V����</param>
        /// <exception cref="WebException"></exception>
        /// <exception cref="TwitterServiceException"></exception>
        public DirectMessages GetDirectMessages(DateTime since)
        {
            return ExecuteRequest<DirectMessages>(() =>
            {
                // Cookie �ł̓_��
                String responseBody = GET(String.Format("/direct_messages.xml?since={0}", Utility.UrlEncode(since.ToUniversalTime().ToString("r"))), false);
                DirectMessages directMessages;
                if (NilClasses.CanDeserialize(responseBody))
                {
                    // ��
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
        /// ���b�Z�[�W��favorites�ɒǉ����܂��B
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Status CreateFavorite(Int32 id)
        {
            return ExecuteRequest<Status>(() =>
            {
                String responseBody = POST(String.Format("/favorites/create/{0}.xml", id), new byte[0]);
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
        /// ���b�Z�[�W��favorites����폜���܂��B
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Status DestroyFavorite(Int32 id)
        {
            return ExecuteRequest<Status>(() =>
            {
                String responseBody = POST(String.Format("/favorites/destroy/{0}.xml", id), new byte[0]);
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

        #region �����^�C�}�[�C�x���g
        /// <summary>
        /// Twitter �̃^�C�����C���̎�M���J�n���܂��B
        /// </summary>
        public void Start()
        {
            _timer.Change(0, Interval * 1000);
            _timerDirectMessage.Change(0, IntervalDirectMessage * 1000);
            if (EnableRepliesCheck)
            {
                _timerReplies.Change(0, IntervalReplies * 1000);
            }
        }

        /// <summary>
        /// Twitter �̃^�C�����C���̎�M���~���܂��B
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
        /// ���Ɏ�M����status���ǂ������`�F�b�N���܂��B���ɑ��M�ς݂̏ꍇfalse��Ԃ��܂��B
        /// </summary>
        /// <param name="statusBuffer"></param>
        /// <param name="status"></param>
        /// <returns></returns>
        private Boolean ProcessDropProtection(LinkedList<Status> statusBuffer, Status status)
        {
            // �����`�F�b�N
            if (_enableDropProtection)
            {
                lock (statusBuffer)
                {
                    if (statusBuffer.Contains(status))
                        return false;

                    statusBuffer.AddLast(status);
                    if (statusBuffer.Count > _bufferSize)
                    {
                        // ��ԌÂ��̂�����
                        //Status oldStatus = null;
                        //foreach (Status statTmp in _statusBuffer)
                        //{
                        //    if (oldStatus == null || oldStatus.CreatedAt > statTmp.CreatedAt)
                        //    {
                        //        oldStatus = statTmp;
                        //    }
                        //}
                        //_statusBuffer.Remove(oldStatus);
                        statusBuffer.RemoveFirst();
                    }
                }
            }

            return true;
        }
        /// <summary>
        /// �X�e�[�^�X�����łɗ����ꂽ���ǂ������`�F�b�N���āA������Ă��Ȃ��ꍇ�Ɏw�肳�ꂽ�A�N�V���������s���܂��B
        /// </summary>
        /// <param name="status"></param>
        /// <param name="action"></param>
        public void ProcessStatus(Status status, Action<Status> action)
        {
            if (ProcessDropProtection(_statusBuffer, status))
            {
                action(status);

                // �ŏI�X�V����
                if (_enableDropProtection)
                {
                    // ��肱�ڂ��h�~���Ă���Ƃ��͈�ԌÂ����t
                    if (status.CreatedAt < _lastAccessTimeline)
                    {
                        _lastAccessTimeline = status.CreatedAt;
                    }
                }
                else
                {
                    if (status.CreatedAt > _lastAccessTimeline)
                    {
                        _lastAccessTimeline = status.CreatedAt;
                    }
                }
            }
        }
        /// <summary>
        /// �X�e�[�^�X�����łɗ����ꂽ���ǂ������`�F�b�N���āA������Ă��Ȃ��ꍇ�Ɏw�肳�ꂽ�A�N�V���������s���܂��B
        /// </summary>
        /// <param name="status"></param>
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
                    // �ŏI�X�V����
                    if (_enableDropProtection)
                    {
                        // ��肱�ڂ��h�~���Ă���Ƃ��͈�ԌÂ����t
                        if (status.CreatedAt < _lastAccessTimeline)
                        {
                            _lastAccessTimeline = status.CreatedAt;
                        }
                    }
                    else
                    {
                        if (status.CreatedAt > _lastAccessTimeline)
                        {
                            _lastAccessTimeline = status.CreatedAt;
                        }
                    }
                });
            }

            if (statusList.Count == 0)
                return;
            tmpStatuses.Status = statusList.ToArray();
            action(tmpStatuses);
        }
        /// <summary>
        /// Replies�X�e�[�^�X�����łɗ����ꂽ���ǂ������`�F�b�N���āA������Ă��Ȃ��ꍇ�Ɏw�肳�ꂽ�A�N�V���������s���܂��B
        /// </summary>
        /// <param name="status"></param>
        /// <param name="action"></param>
        public void ProcessRepliesStatus(Statuses statuses, Action<Statuses> action)
        {
            Statuses tmpStatuses = new Statuses();
            List<Status> statusList = new List<Status>();
            foreach (Status status in statuses.Status)
            {
                if (status.CreatedAt < _lastAccessReplies)
                    continue;

                if (ProcessDropProtection(_repliesBuffer, status) && ProcessDropProtection(_statusBuffer, status))
                {
                    statusList.Add(status);

                    // �ŏI�X�V����
                    if (_enableDropProtection)
                    {
                        // ��肱�ڂ��h�~���Ă���Ƃ��͈�ԌÂ����t
                        if (status.CreatedAt < _lastAccessTimeline)
                        {
                            _lastAccessTimeline = status.CreatedAt;
                        }
                    }
                    else
                    {
                        if (status.CreatedAt > _lastAccessTimeline)
                        {
                            _lastAccessTimeline = status.CreatedAt;
                        }
                    }
                }
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
                Statuses statuses = GetTimeline(_lastAccessTimeline);
                Array.Reverse(statuses.Status);
                // �����`�F�b�N
                ProcessStatuses(statuses, (s) =>
                {
                    OnTimelineStatusesReceived(new StatusesUpdatedEventArgs(s, _isFirstTime, friendsCheckRequired));
                });

                if (_isFirstTime && _enableDropProtection)
                {
                    _lastAccessTimeline = DateTime.Now;
                }
                _isFirstTime = false;
            });
        }

        private void CheckDirectMessage()
        {
            RunCheck(delegate
            {
                DirectMessages directMessages = GetDirectMessages(_lastAccessDirectMessage);
                Array.Reverse(directMessages.DirectMessage);
                foreach (DirectMessage message in directMessages.DirectMessage)
                {
                    // �`�F�b�N
                    if (message == null || String.IsNullOrEmpty(message.SenderScreenName))
                    {
                        continue;
                    }
                    
                    OnDirectMessageReceived(new DirectMessageEventArgs(message));
                    
                    // �ŏI�X�V����
                    if (message.CreatedAt > _lastAccessDirectMessage)
                    {
                        _lastAccessDirectMessage = message.CreatedAt;
                    }
                }
            });
        }

        private void CheckNewReplies()
        {
            Boolean friendsCheckRequired = false;
            RunCheck(delegate
            {
                Statuses statuses = GetReplies();
                Array.Reverse(statuses.Status);
                bool dummy = false;
                
                // �����`�F�b�N
                ProcessRepliesStatus(statuses, (s) =>
                {
                    // Here I pass dummy, because no matter how the replier flags
                    // friendsCheckRequired, we cannot receive his or her info
                    // through get_friends.
                    OnRepliesReceived(new StatusesUpdatedEventArgs(s, _isFirstTimeReplies, friendsCheckRequired));
                });

                if (_isFirstTimeReplies && _enableDropProtection)
                {
                    _lastAccessReplies = DateTime.Now;
                }
                _isFirstTimeReplies = false;
            });
        }

        #endregion

        #region �C�x���g
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

        #region ���[�e�B���e�B���\�b�h
        /// <summary>
        /// 
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private static String EncodeMessage(String s)
        {
            return Utility.UrlEncode(s);
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
        /// �`�F�b�N�����s���܂��B��O�����������ꍇ�ɂ͎����I�Ƀ��b�Z�[�W�𑗐M���܂��B
        /// </summary>
        /// <param name="proc">���s����`�F�b�N����</param>
        /// <returns></returns>
        private Boolean RunCheck(Procedure proc)
        {
            try
            {
                proc();
            }
            catch (WebException ex)
            {
                if (ex.Response == null || !(ex.Response is HttpWebResponse) || ((HttpWebResponse)(ex.Response)).StatusCode != HttpStatusCode.NotModified)
                {
                    // not-modified �ȊO
                    OnCheckError(new ErrorEventArgs(ex));
                    return false;
                }
            }
            catch (TwitterServiceException ex2)
            {
                OnCheckError(new ErrorEventArgs(ex2));
                return false;
            }

            return true;
        }

        /// <summary>
        /// �^�C�}�[�R�[���o�b�N�̏��������s���܂��B
        /// </summary>
        /// <param name="timer"></param>
        /// <param name="callbackProcedure"></param>
        private void RunCallback(Timer timer, Procedure callbackProcedure)
        {
            // ���܂�ɏ������x���Ɠ�d�ɂȂ�\��������
            if (Monitor.TryEnter(timer))
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

        #region IDisposable �����o

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
                // ���̃A�v���P�[�V������ HttpWebReqeust �ȊO�����邱�Ƃ͂Ȃ�
                HttpWebRequest webRequest = base.GetWebRequest(address) as HttpWebRequest;
                webRequest.PreAuthenticate = true;
                webRequest.Accept = "text/xml, application/xml";
                webRequest.UserAgent = String.Format("{0}/{1}", _twitterService.ClientName, GetType().Assembly.GetName().Version);
                //webRequest.Referer = TwitterService.Referer;
                webRequest.Headers["X-Twitter-Client"] = _twitterService.ClientName;
                webRequest.Headers["X-Twitter-Client-Version"] = _twitterService.ClientVersion;
                webRequest.Headers["X-Twitter-Client-URL"] = _twitterService.ClientUrl;

                return webRequest;
            }
        }

        /// <summary>
        /// �w�肳�ꂽURL����f�[�^���擾��������Ƃ��ĕԂ��܂��BCookieLoginMode���L���ȂƂ��͎����I��Cookie���O�C����ԂŎ擾���܂��B
        /// </summary>
        /// <param name="url">�f�[�^���擾����URL</param>
        /// <returns></returns>
        public String GET(String url)
        {
            return GET(url, POSTFetchMode);
        }

        /// <summary>
        /// �w�肳�ꂽURL����f�[�^���擾��������Ƃ��ĕԂ��܂��B
        /// </summary>
        /// <param name="url">�f�[�^���擾����URL</param>
        /// <param name="postFetchMode">POST�Ŏ擾���邩�ǂ���</param>
        /// <returns></returns>
        public String GET(String url, Boolean postFetchMode)
        {
            if (postFetchMode)
            {
                return POST(url, new Byte[0]);
            }
            else
            {
                url = ServiceServerPrefix + url;
                System.Diagnostics.Trace.WriteLine("GET: " + url);
                HttpWebRequest webRequest = CreateHttpWebRequest(url, "GET");
                HttpWebResponse webResponse = webRequest.GetResponse() as HttpWebResponse;
                using (StreamReader sr = new StreamReader(webResponse.GetResponseStream()))
                    return sr.ReadToEnd();
            }
        }

        public String POST(String url, Byte[] postData)
        {
            url = ServiceServerPrefix + url;
            System.Diagnostics.Trace.WriteLine("POST: " + url);
            HttpWebRequest webRequest = CreateHttpWebRequest(url, "POST");
            using (Stream stream = webRequest.GetRequestStream())
            {
                stream.Write(postData, 0, postData.Length);
            }
            HttpWebResponse webResponse = webRequest.GetResponse() as HttpWebResponse;
            using (StreamReader sr = new StreamReader(webResponse.GetResponseStream()))
                return sr.ReadToEnd();
        }

        protected virtual HttpWebRequest CreateHttpWebRequest(String url, String method)
        {
            HttpWebRequest webRequest = HttpWebRequest.Create(url) as HttpWebRequest;
            //webRequest.Credentials = _credential;
            //webRequest.PreAuthenticate = true;
            webRequest.Proxy = _proxy;
            webRequest.Method = method;
            webRequest.Accept = "text/xml, application/xml";
            webRequest.UserAgent = String.Format("{0}/{1}", ClientName, ClientVersion);
            //webRequest.Referer = TwitterService.Referer;
            webRequest.Headers["X-Twitter-Client"] = ClientName;
            webRequest.Headers["X-Twitter-Client-Version"] = ClientVersion;
            webRequest.Headers["X-Twitter-Client-URL"] = ClientUrl;

            Uri uri = new Uri(url);

            NetworkCredential cred = _credential.GetCredential(uri, "Basic");
            webRequest.Headers["Authorization"] = String.Format("Basic {0}", Convert.ToBase64String(Encoding.UTF8.GetBytes(String.Format("{0}:{1}", cred.UserName, cred.Password))));

            return webRequest as HttpWebRequest;
        }

        #region Cookie �A�N�Z�X

        private CookieCollection _cookies = null;
        public String GETWithCookie(String url)
        {
            Boolean isRetry = false;
            url = ServiceServerPrefix + url;
        Retry:
            try
            {
                System.Diagnostics.Trace.WriteLine(String.Format("GET(Cookie): {0}", url));
                return DownloadString(url);
            }
            catch (WebException we)
            {
                HttpWebResponse wResponse = we.Response as HttpWebResponse;
                if (wResponse == null || wResponse.StatusCode != HttpStatusCode.Unauthorized || isRetry)
                    throw;

                _cookies = Login(_userName, _credential.GetCredential(new Uri("http://twitter.com"), "Basic").Password);

                isRetry = true;
                goto Retry;
            }
        }

        private CookieCollection Login(String userNameOrEmail, String password)
        {
            System.Diagnostics.Trace.WriteLine(String.Format("Cookie Login: {0}", userNameOrEmail));

            HttpWebRequest request = CreateWebRequest("http://twitter.com/sessions") as HttpWebRequest;
            request.AllowAutoRedirect = false;
            request.Method = "POST";
            using (StreamWriter sw = new StreamWriter(request.GetRequestStream()))
            {
                sw.Write("username_or_email={0}&password={1}&remember_me=1&commit=Sign%20In", userNameOrEmail, password);
            }
            
            HttpWebResponse response = request.GetResponse() as HttpWebResponse;
            using (StreamReader sr = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                String responseBody = sr.ReadToEnd();

                if (response.Cookies.Count == 0)
                {
                    throw new ApplicationException("���O�C���Ɏ��s���܂����B���[�U���܂��̓p�X���[�h���Ԉ���Ă���\��������܂��B");
                }

                foreach (Cookie cookie in response.Cookies)
                {
                    cookie.Domain = "twitter.com";
                }
                return response.Cookies;
            }
        }

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
                if (_cookies != null)
                {
                    httpRequest.CookieContainer.Add(_cookies);
                }
            }
            return request;
        }
           
#if FALSE
        private CookieCollection Login(String userNameOrEmail, String password)
        {
            System.Diagnostics.Trace.WriteLine(String.Format("Cookie Login: {0}", userNameOrEmail));
            using (CookieEnabledWebClient webClient = new CookieEnabledWebClient())
            {
                Byte[] data = webClient.UploadData("https://twitter.com/sessions", Encoding.UTF8.GetBytes(
                    String.Format("username_or_email={0}&password={1}&remember_me=1&commit=Sign%20In", userNameOrEmail, password)
                ));

                String responseBody = Encoding.UTF8.GetString(data);

                if (webClient.Cookies == null)
                {
                    throw new ApplicationException("���O�C���Ɏ��s���܂����B���[�U���܂��̓p�X���[�h���Ԉ���Ă���\��������܂��B");
                }

                // XXX: .twitter.com �ƂȂ��Ă���� twitter.com �ɑ����Ȃ��̂ŏ���������
                foreach (Cookie cookie in webClient.Cookies)
                {
                    cookie.Domain = "twitter.com";
                }

                return webClient.Cookies;
            }
        }
        class CookieEnabledWebClient : WebClient
        {
            public CookieEnabledWebClient()
                : base()
            {
            }
            public CookieEnabledWebClient(CookieCollection cookies)
                : base()
            {
                _cookies = cookies;
            }
            private CookieCollection _cookies;
            public CookieCollection Cookies
            {
                get
                {
                    return _cookies;
                }
                set
                {
                    _cookies = value;
                }
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                if (request is HttpWebRequest)
                {
                    HttpWebRequest httpRequest = request as HttpWebRequest;
                    httpRequest.UserAgent = "Mozilla/4.0 (compatible; MSIE 7.0; Windows NT 5.1)";
                    httpRequest.Referer = Referer;
                    httpRequest.PreAuthenticate = false;
                    httpRequest.Accept = "*/*";
                    httpRequest.CookieContainer = new CookieContainer();
                    if (_cookies != null)
                    {
                        httpRequest.CookieContainer.Add(_cookies);
                    }
                }
                return request;
            }

            protected override WebResponse GetWebResponse(WebRequest request)
            {
                WebResponse response = base.GetWebResponse(request);
                if (response is HttpWebResponse)
                {
                    HttpWebResponse httpResponse = response as HttpWebResponse;
                    _cookies = httpResponse.Cookies;
                }
                return response;
            }
        }
#endif
        
        String DownloadString(String url)
        {
            WebRequest request = CreateWebRequest(url);
            WebResponse response = null;
            try
            {
                response = request.GetResponse();
                using (StreamReader sr = new StreamReader(response.GetResponseStream()))
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
#if FALSE
            using (CookieEnabledWebClient webClient = new CookieEnabledWebClient(_cookies))
            {
                return webClient.DownloadString(url);
            }
#endif
        }
        #endregion
    }

    public class ErrorEventArgs : EventArgs
    {
        public Exception Exception { get; set; }
        public ErrorEventArgs(Exception ex)
        {
            this.Exception = ex;
        }
    }

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

    [XmlRoot("nil-classes")]
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
    }

    [XmlType("DirectMessage")]
    public class DirectMessage
    {
        [XmlElement("id")]
        public Int32 Id;
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
    }

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
    }

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
    }

    [XmlType("status")]
    public class Status
    {
        [XmlElement("created_at")]
        public String _createdAtOriginal;
        [XmlElement("id")]
        public Int32 Id;
        [XmlElement("text")]
        public String _textOriginal;
        [XmlElement("user")]
        public User User;

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

                return _text;
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
        public static XmlSerializer Serializer
        {
            get
            {
                return _serializer;
            }
        }

        public override int GetHashCode()
        {
            return this.Id;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Status))
                return false;

            Status status = obj as Status;
            return (status.Id == this.Id) && (status.Text == this.Text);
        }
    }
}
