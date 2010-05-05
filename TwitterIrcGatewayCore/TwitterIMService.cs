#if ENABLE_IM_SUPPORT
using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

using agsXMPP;
using agsXMPP.Xml.Dom;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    [Obsolete("IMのサポートは現在利用できません。")]
    public class TwitterIMService : IDisposable
    {
        private XmppClientConnection _xmppConnection;
        public static String ServiceSender = "twitter@twitter.com";

        public event EventHandler<StatusUpdateReceivedEventArgs> StatusUpdateReceived;
        public event EventHandler Logined;
        public event EventHandler<ErrorEventArgs> ErrorHandled;
        public event EventHandler<ErrorEventArgs> SocketErrorHandled;
        public event EventHandler Closed;
        public event EventHandler AuthErrored;
        
        public TwitterIMService(String connectServerName, String serverName, String userName, String password)
        {
            _xmppConnection = new XmppClientConnection
            {
                ConnectServer = connectServerName,
                Server = serverName,
                Username = userName,
                Password = password,
            };
            _xmppConnection.OnXmppConnectionStateChanged += new XmppConnectionStateHandler(_xmppConnection_OnXmppConnectionStateChanged);
            _xmppConnection.OnClose += new ObjectHandler(_xmppConnection_OnClose);
            _xmppConnection.OnLogin += new ObjectHandler(xmppConnection_OnLogin);
            _xmppConnection.OnAuthError += new XmppElementHandler(xmppConnection_OnAuthError);
            _xmppConnection.OnError += new ErrorHandler(xmppConnection_OnError);
            _xmppConnection.OnMessage += new agsXMPP.protocol.client.MessageHandler(xmppConnection_OnMessage);
            _xmppConnection.OnSocketError += new ErrorHandler(xmppConnection_OnSocketError);
            _xmppConnection.OnStreamError += new XmppElementHandler(xmppConnection_OnStreamError);
        }

        void _xmppConnection_OnXmppConnectionStateChanged(object sender, XmppConnectionState state)
        {
            Trace.WriteLine(String.Format("XMPPConnection State: {0}", state));
        }

        public void Open()
        {
            _xmppConnection.Open();
        }

        public void Close()
        {
            _xmppConnection.Close();
        }

        #region XMPP Events
        void xmppConnection_OnMessage(object sender, agsXMPP.protocol.client.Message msg)
        {
            //Trace.WriteLine(msg.ToString());
            if (msg.From.Bare != ServiceSender)
                return;
            
            Element entryE = msg.SelectSingleElement("entry");
            Element sourceE = entryE.SelectSingleElement("source");
            Element authorE = sourceE.SelectSingleElement("author");

            User user = new User
            {
                Description = authorE.GetTag("description"),
                Id = authorE.GetTagInt("twitter_id"),
                Location = authorE.GetTag("location"),
                Protected = authorE.GetTagBool("protected"),
                Name = authorE.GetTag("name"),
                ScreenName = authorE.GetTag("screen_name"),
                Url = authorE.GetTag("url"),
                ProfileImageUrl = authorE.GetTag("profile_image_url")
            };

            String body = msg.Body;
            if (body.IndexOf(": ") > -1)
                body = body.Substring(body.IndexOf(": ") + 2);
            
            Status status = new Status
            {
                CreatedAt = DateTime.Parse(entryE.GetTag("published")),
                Text = body,
                User = user,
                Id = entryE.GetTagInt((entryE.GetTagInt("twitter_id") == 0 ? "status_id" : "twitter_id")) // HACK: 何故かどっちかでくる
            };

            OnStatusUpdateReceived(status);
        }

        void _xmppConnection_OnClose(object sender)
        {
            OnClosed();
        }

        void xmppConnection_OnLogin(object sender)
        {
            OnLogined();
        }

        void xmppConnection_OnAuthError(object sender, Element e)
        {
            OnAuthErrored();
        }

        void xmppConnection_OnSocketError(object sender, Exception ex)
        {
            OnSocketErrorHandled(ex);
        }

        void xmppConnection_OnStreamError(object sender, Element e)
        {
            Trace.WriteLine(String.Format("XMPPConnection OnStreamError: {0}", e));
        }

        void xmppConnection_OnError(object sender, Exception ex)
        {
            OnErrorHandled(ex);
        }
        #endregion

        #region Events
        private void OnLogined()
        {
            if (Logined != null)
                Logined(this, EventArgs.Empty);
        }
        private void OnErrorHandled(Exception ex)
        {
            if (ErrorHandled != null)
                ErrorHandled(this, new ErrorEventArgs(ex));
        }
        private void OnSocketErrorHandled(Exception ex)
        {
            if (SocketErrorHandled != null)
                SocketErrorHandled(this, new ErrorEventArgs(ex));
        }
        private void OnAuthErrored()
        {
            if (AuthErrored != null)
                AuthErrored(this, EventArgs.Empty);
        }
        private void OnStatusUpdateReceived(Status status)
        {
            if (StatusUpdateReceived != null)
                StatusUpdateReceived(this, new StatusUpdateReceivedEventArgs(status));
        }
        private void OnClosed()
        {
            if (Closed != null)
                Closed(this, EventArgs.Empty);
        }

        public class ErrorEventArgs : EventArgs
        {
            public Exception Exception { get; set; }

            public ErrorEventArgs(Exception ex)
            {
                this.Exception = ex;
            }
        }

        public class StatusUpdateReceivedEventArgs : EventArgs
        {
            public Status Status { get; set; }

            public StatusUpdateReceivedEventArgs(Status status)
            {
                this.Status = status;
            }
        }
        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            Close();
        }

        #endregion
    }
}
#endif