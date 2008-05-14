using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Net;
using Misuzilla.Net.Irc;
using System.Web;
using System.Threading;
using System.Xml;
using System.Diagnostics;
using System.Windows.Forms;
using System.Reflection;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    class Program : ApplicationContext
    {
        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
            Program program = new Program();
            if (program.Initialize())
            {
                Application.Run(program);
            }
        }

        private Settings _settings = new Settings();
        private Server _server;
        private NotifyIcon _notifyIcon;
        private const String Name = "Twitter IRC Gateway Server";

        public Boolean Initialize()
        {
            Application.ApplicationExit += new EventHandler(Application_ApplicationExit);
            Int32 port = _settings.Port;
            IPAddress ipAddr = _settings.LocalOnly ? IPAddress.Loopback : IPAddress.Any;

            ContextMenu ctxMenu = new ContextMenu();
            ctxMenu.MenuItems.Add(new MenuItem("�I��(&X)", ContextMenuItemExit));
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = String.Format("{0} (IP: {1} / �|�[�g {2})", Name, ipAddr, port);
            _notifyIcon.ContextMenu = ctxMenu;
            _notifyIcon.Icon = Resource.ApplicationIcon;

            _server = new Server();
            _server.EnableTrace = _settings.EnableTrace;
            _server.IgnoreWatchError = _settings.IgnoreWatchError;
            _server.Interval = _settings.Interval;
            _server.ResolveTinyUrl = _settings.ResolveTinyUrl;
            _server.EnableDropProtection = _settings.EnableDropProtection;
            _server.SetTopicOnStatusChanged = _settings.SetTopicOnStatusChanged;
            _server.IntervalDirectMessage = _settings.IntervalDirectMessage;
            _server.CookieLoginMode = _settings.CookieLoginMode;
            _server.ChannelName = "#"+_settings.TwitterChannelName;
            _server.EnableRepliesCheck = _settings.EnableRepliesCheck;
            _server.IntervalReplies = _settings.IntervalReplies;
            _server.DisableUserList = _settings.DisableUserList;
            _server.BroadcastUpdate = _settings.BroadcastUpdate;
            _server.ClientMessageWait = _settings.ClientMessageWait;
            _server.BroadcastUpdateMessageIsNotice = _settings.BroadcastUpdateMessageIsNotice;
            _server.SessionStartedRecieved += new EventHandler<SessionStartedEventArgs>(_server_SessionStartedRecieved);
            try
            {
                _server.Encoding = (String.Compare(_settings.Charset, "UTF-8", true) == 0)
                    ? new UTF8Encoding(false) // BOM �Ȃ�
                    : Encoding.GetEncoding(_settings.Charset);
            }
            catch (ArgumentException)
            {
                _server.Encoding = Encoding.GetEncoding("ISO-2022-JP");
            }

            // start
            try
            {
                _server.Start(ipAddr, port);
                _notifyIcon.Visible = true;
                _notifyIcon.ShowBalloonTip(1000 * 10, Name, String.Format("IRC�T�[�o���|�[�g {0} �ŊJ�n����܂����B", port), ToolTipIcon.Info);
            }
            catch (SocketException se)
            {
                MessageBox.Show(se.Message, Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        void Application_ApplicationExit(object sender, EventArgs e)
        {
            _notifyIcon.Visible = false;
            _server.Stop();
        }

        void _server_SessionStartedRecieved(object sender, SessionStartedEventArgs e)
        {
            _notifyIcon.ShowBalloonTip(1000 * 10, Name, String.Format("���[�U {0} ���T�[�o�ɐڑ����܂����B", e.UserName), ToolTipIcon.Info);
        }

        private void ContextMenuItemExit(Object sender, EventArgs e)
        {
            Application.Exit();
        }

        /// <summary>
        /// �n���h������Ă��Ȃ���O���L���b�`���āA�X�^�b�N�g���[�X��ۑ����ăf�o�b�O�ɖ𗧂Ă�
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception; // Exception �ȊO�����ł���̂͒�����ȏꍇ�̂݁B

            if (MessageBox.Show(
                String.Format("�A�v���P�[�V�����̎��s���ɗ\�����Ȃ��d��ȃG���[���������܂����B\n\n�G���[���e:\n{0}\n\n�G���[�����t�@�C���ɕۑ����A�񍐂��Ă����������Ƃŕs��̉����ɖ𗧂\��������܂��B�G���[�����t�@�C���ɕۑ����܂���?",
                ((Exception)(e.ExceptionObject)).Message)
                , Application.ProductName
                , MessageBoxButtons.YesNo
                , MessageBoxIcon.Error
                , MessageBoxDefaultButton.Button1
                ) == DialogResult.Yes)
            {
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.DefaultExt = "txt";
                    saveFileDialog.Filter = "�e�L�X�g�t�@�C��|*.txt";
                    saveFileDialog.FileName = String.Format("twitterircgateway_stacktrace_{0:yyyyMMdd_HHmmss}.txt", DateTime.Now);
                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        using (Stream stream = saveFileDialog.OpenFile())
                        using (StreamWriter sw = new StreamWriter(stream))
                        {
                            Assembly asm = Assembly.GetExecutingAssembly();

                            sw.WriteLine("��������: {0}", DateTime.Now);
                            sw.WriteLine();
                            sw.WriteLine("TwitterIrcGateway:");
                            sw.WriteLine("========================");
                            sw.WriteLine("�o�[�W����: {0}", Assembly.GetExecutingAssembly().GetName().Version);
                            sw.WriteLine("�A�Z���u��: {0}", Assembly.GetExecutingAssembly().FullName);
                            sw.WriteLine();
                            sw.WriteLine("�����:");
                            sw.WriteLine("========================");
                            sw.WriteLine("�I�y���[�e�B���O�V�X�e��: {0}", Environment.OSVersion);
                            sw.WriteLine("Microsoft .NET Framework: {0}", Environment.Version);
                            sw.WriteLine(); 
                            sw.WriteLine("�n���h������Ă��Ȃ���O: ");
                            sw.WriteLine("=========================");
                            sw.WriteLine(ex.ToString());
                        }
                    }
                }

            }
        }

    }
}
