using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    public class Server
    {
        private TcpListener _tcpListener;
        private List<Session> _sessions;
        private Encoding _encoding = Encoding.GetEncoding("ISO-2022-JP");
        
        /// <summary>
        /// �`�F�b�N����Ԋu
        /// </summary>
        public Int32 Interval = 60;

        /// <summary>
        /// �_�C���N�g���b�Z�[�W���`�F�b�N����Ԋu
        /// </summary>
        public Int32 IntervalDirectMessage = 60 * 5;

        /// <summary>
        /// Replies���`�F�b�N���邩�ǂ���
        /// </summary>
        public Boolean EnableRepliesCheck = false;
        
        /// <summary>
        /// Replies�`�F�b�N����Ԋu
        /// </summary>
        public Int32 IntervalReplies = 60 * 5;

        /// <summary>
        /// �G���[�𖳎����邩�ǂ���
        /// </summary>
        public Boolean IgnoreWatchError = false;

        /// <summary>
        /// TinyURL��W�J���邩�ǂ���
        /// </summary>
        public Boolean ResolveTinyUrl = true;

        /// <summary>
        /// ��肱�ڂ��h�~�𗘗p���邩�ǂ���
        /// </summary>
        public Boolean EnableDropProtection = true;

        /// <summary>
        /// �X�e�[�^�X���X�V�����Ƃ��Ƀg�s�b�N��ύX���邩�ǂ���
        /// </summary>
        public Boolean SetTopicOnStatusChanged = false;

        /// <summary>
        /// �g���[�X��L���ɂ��邩�ǂ���
        /// </summary>
        public Boolean EnableTrace = false;

        /// <summary>
        /// Cookie ���O�C���Ń^�C�����C�����擾���邩�ǂ���
        /// </summary>
        public Boolean CookieLoginMode = false;

        /// <summary>
        /// Twitter�̃X�e�[�^�X�������`�����l����
        /// </summary>
        public String ChannelName = "#twitter";

        /// <summary>
        /// ���[�U�ꗗ���擾���邩�ǂ���
        /// </summary>
        public Boolean DisableUserList = false;

        /// <summary>
        /// �A�b�v�f�[�g�����ׂẴ`�����l���ɓ����邩�ǂ���
        /// </summary>
        public Boolean BroadcastUpdate = false;

        /// <summary>
        /// �N���C�A���g�Ƀ��b�Z�[�W�𑗐M����Ƃ��̃E�F�C�g
        /// </summary>
        public Int32 ClientMessageWait = 0;

        /// <summary>
        /// �A�b�v�f�[�g�����ׂẴ`�����l���ɓ�����Ƃ�NOTICE�ɂ��邩�ǂ���
        /// </summary>
        public Boolean BroadcastUpdateMessageIsNotice = false;

        /// <summary>
        /// API�A�N�Z�X�ɗ��p����v���N�V�T�[�o�̐ݒ�
        /// </summary>
        public IWebProxy Proxy = null;

        public const String ServerName = "localhost";
        public const String ServerNick = "$TwitterIrcGatewayServer$";

        public event EventHandler<SessionStartedEventArgs> SessionStartedRecieved;

        void AcceptHandled(IAsyncResult ar)
        {
            if (_tcpListener != null && ar.IsCompleted)
            {
                TcpClient tcpClient = _tcpListener.EndAcceptTcpClient(ar);
                _tcpListener.BeginAcceptTcpClient(AcceptHandled, this);

                Trace.WriteLine(String.Format("Client Connected: RemoteEndPoint={0}", tcpClient.Client.RemoteEndPoint));
                Session session = new Session(this, tcpClient);
                lock (_sessions)
                {
                    _sessions.Add(session);
                }
                session.SessionStarted += new EventHandler<SessionStartedEventArgs>(session_SessionStartedRecieved);
                session.SessionEnded += new EventHandler(session_SessionEnded);
                session.Start();
            }
        }

        void session_SessionEnded(object sender, EventArgs e)
        {
            lock (_sessions)
            {
                _sessions.Remove(sender as Session);
            }
        }

        void session_SessionStartedRecieved(object sender, SessionStartedEventArgs e)
        {
            // ���p
            if (SessionStartedRecieved != null)
            {
                SessionStartedRecieved(sender, e);
            }
        }

        public Encoding Encoding
        {
            get { return _encoding; }
            set { _encoding = value; }
        }
        public Boolean IsRunning
        {
            get { return _tcpListener != null; }
        }

        public void Start(IPAddress ipAddr, Int32 port)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException();
            }

            _sessions = new List<Session>();

            Trace.WriteLine(String.Format("Starting IRC Server: IPAddress = {0}, port = {1}", ipAddr, port));
            _tcpListener = new TcpListener(ipAddr, port);
            _tcpListener.Start();
            _tcpListener.BeginAcceptTcpClient(AcceptHandled, this);
        }

        public void Stop()
        {
            lock (_sessions)
            {
                foreach (Session session in _sessions)
                {
                    session.Close();
                }
            }
            if (_tcpListener != null)
            {
                _tcpListener.Stop();
                _tcpListener = null;
            }
        }
    }
}
