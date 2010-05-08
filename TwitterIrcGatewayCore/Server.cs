using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using Misuzilla.Applications.TwitterIrcGateway.Authentication;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    /// <summary>
    /// TwitterIrcGatewayの接続サーバ機能を提供します。
    /// </summary>
    public class Server : MarshalByRefObject
    {
        private static List<Server> _runningServers = new List<Server>();
        private static Dictionary<Int32, Session> _sessions;
        
        private TcpListener _tcpListener;
        private Encoding _encoding = Encoding.GetEncoding("ISO-2022-JP");

        /// <summary>
        /// APIアクセスに利用するプロクシサーバの設定
        /// </summary>
        public IWebProxy Proxy = null;

        public const String ServerName = "localhost";
        public const String ServerNick = "$TweetIrcGatewayServer$";

        /// <summary>
        /// ユーザ認証を行うクラスを取得・設定します
        /// </summary>
        public IAuthentication Authentication { get; set; }

        /// <summary>
        /// SSL通信を必要とするかどうかを取得します
        /// </summary>
        public Boolean IsSslConnection { get; private set; }
        /// <summary>
        /// SSLの認証に利用する証明書を取得・設定します
        /// </summary>
        public X509Certificate Certificate { get; set; }

        /// <summary>
        /// 新たなセッションが開始されたイベント
        /// </summary>
        public event EventHandler<ConnectionAttachEventArgs> ConnectionAttached;

        /// <summary>
        /// 文字エンコーディングを取得・設定します
        /// </summary>
        public Encoding Encoding
        {
            get { return _encoding; }
            set { _encoding = value; }
        }
        /// <summary>
        /// サーバが現在動作中かどうかを取得します
        /// </summary>
        public Boolean IsRunning
        {
            get { return _tcpListener != null; }
        }

        /// <summary>
        /// 現在存在しているセッション情報のコレクションを取得します。
        /// </summary>
        public IDictionary<Int32, Session> Sessions
        {
            get { return _sessions; }
        }

        /// <summary>
        /// 現在実行しているサーバのコレクションを取得します。
        /// </summary>
        public static IList<Server> RunningServers
        {
            get { return _runningServers.AsReadOnly(); }
        }

        public Server() : this(false)
        {
        }

        public Server(Boolean useSslConnection)
        {
            // for Mono
            if (Environment.OSVersion.Platform == PlatformID.Unix)
                ServicePointManager.ServerCertificateValidationCallback += delegate { return true; };

            ServicePointManager.DefaultConnectionLimit = 1000;
            Authentication = new XAuthAuthentication();
            IsSslConnection = useSslConnection;
        }
        
        /// <summary>
        /// 指定したIPアドレスとポートでクライアントからの接続待ち受けを開始します
        /// </summary>
        /// <param name="ipAddr">接続を待ち受けるIPアドレス</param>
        /// <param name="port">接続を待ち受けるポート</param>
        public void Start(IPAddress ipAddr, Int32 port)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException();
            }
            
            lock (_runningServers)
            {
                _runningServers.Add(this);
            }

            _sessions = new Dictionary<Int32, Session>();

            TraceLogger.Server.Information(String.Format("Starting IRC Server: IPAddress = {0}, Port = {1}, IsSslConnection={2}", ipAddr, port, IsSslConnection));
            _tcpListener = new TcpListener(ipAddr, port);
            _tcpListener.Start();
            _tcpListener.BeginAcceptTcpClient(AcceptHandled, this);
        }
        
        /// <summary>
        /// クライアントからの接続待ち受けを停止します
        /// </summary>
        public void StopListen()
        {
            lock (this)
            {
                if (_tcpListener != null)
                {
                    _tcpListener.Stop();
                    _tcpListener = null;
                }
            }
        }
        
        /// <summary>
        /// クライアントからの接続待ち受けを停止し、すべてのセッションを停止します。
        /// </summary>
        public void Stop()
        {
            lock (this)
            {
                StopListen();
                
                lock (_runningServers)
                {
                    _runningServers.Remove(this);
                }
                lock (_sessions)
                {
                    List<Session> sessions = new List<Session>(_sessions.Values);
                    foreach (Session session in sessions)
                    {
                        session.Close();
                    }
                }
            }
        }

        public override string ToString()
        {
            return String.Format("Server: LocalEndPoint={0}", _tcpListener.LocalEndpoint);
        }
        
        public Session GetOrCreateSession(User user)
        {
            Int32 id = user.Id;
            lock (_sessions)
            {
                if (!_sessions.ContainsKey(id))
                {
                    _sessions[id] = new Session(user, this);
                    _sessions[id].ConnectionAttached += new EventHandler<ConnectionAttachEventArgs>(Server_ConnectionAttached);
                }
                return _sessions[id];
            }
        }

        void Server_ConnectionAttached(object sender, ConnectionAttachEventArgs e)
        {
            if (ConnectionAttached != null)
            {
                ConnectionAttached(sender, e);
            }
        }

        #region Internal Implementation
        void AcceptHandled(IAsyncResult ar)
        {
            TcpClient tcpClient = null;

            if (_tcpListener != null)
            {
                lock (this)
                    lock (_tcpListener)
                    {
                        if (_tcpListener != null && ar.IsCompleted)
                        {
                            tcpClient = _tcpListener.EndAcceptTcpClient(ar);
                            _tcpListener.BeginAcceptTcpClient(AcceptHandled, this);
                        }
                    }
            }

            if (tcpClient != null && tcpClient.Connected)
            {
                TraceLogger.Server.Information(String.Format("Client Connected: RemoteEndPoint={0}", tcpClient.Client.RemoteEndPoint));
                Connection connection = new Connection(this, tcpClient);
                connection.Start();
            }
        }
        #endregion
    }
}
