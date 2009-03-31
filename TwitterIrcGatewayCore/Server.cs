using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    /// <summary>
    /// TwitterIrcGateway�̐ڑ��T�[�o�@�\��񋟂��܂��B
    /// </summary>
    public class Server : MarshalByRefObject
    {
        private TcpListener _tcpListener;
        private List<Session> _sessions;
        private Encoding _encoding = Encoding.GetEncoding("ISO-2022-JP");

        /// <summary>
        /// API�A�N�Z�X�ɗ��p����v���N�V�T�[�o�̐ݒ�
        /// </summary>
        public IWebProxy Proxy = null;

        public const String ServerName = "localhost";
        public const String ServerNick = "$TwitterIrcGatewayServer$";

        /// <summary>
        /// �V���ȃZ�b�V�������J�n���ꂽ�C�x���g
        /// </summary>
        public event EventHandler<SessionStartedEventArgs> SessionStartedReceived;

        /// <summary>
        /// �����G���R�[�f�B���O���擾�E�ݒ肵�܂�
        /// </summary>
        public Encoding Encoding
        {
            get { return _encoding; }
            set { _encoding = value; }
        }
        /// <summary>
        /// �T�[�o�����ݓ��쒆���ǂ������擾���܂�
        /// </summary>
        public Boolean IsRunning
        {
            get { return _tcpListener != null; }
        }

        /// <summary>
        /// �w�肵��IP�A�h���X�ƃ|�[�g�ŃN���C�A���g����̐ڑ��҂��󂯂��J�n���܂�
        /// </summary>
        /// <param name="ipAddr">�ڑ���҂��󂯂�IP�A�h���X</param>
        /// <param name="port">�ڑ���҂��󂯂�|�[�g</param>
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
        
        /// <summary>
        /// �N���C�A���g����̐ڑ��҂��󂯂��~���܂�
        /// </summary>
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

        #region Internal Implementation
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
                session.SessionStarted += new EventHandler<SessionStartedEventArgs>(session_SessionStartedReceived);
                session.SessionEnded += new EventHandler<EventArgs>(session_SessionEnded);
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

        void session_SessionStartedReceived(object sender, SessionStartedEventArgs e)
        {
            // ���p
            if (SessionStartedReceived != null)
            {
                SessionStartedReceived(sender, e);
            }
        }
        #endregion
    }
}
