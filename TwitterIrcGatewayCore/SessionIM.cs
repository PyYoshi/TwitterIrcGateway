using System;
using System.Collections.Generic;
using System.Text;

namespace Misuzilla.Applications.TwitterIrcGateway
{
#if ENABLE_IM_SUPPORT
    public partial class Session
    {
        private TwitterIMService _twitterIm;
    
        private Boolean _requireIMReconnect = false;
        private Int32 _imReconnectCount = 0;
    #region Twitter IM Service
        void MessageReceived_TIGIMENABLE(object sender, MessageReceivedEventArgs e)
        {
            if (String.Compare(e.Message.Command, "TIGIMENABLE", true) != 0) return;
            if (String.IsNullOrEmpty(e.Message.CommandParams[3]))
            {
                SendTwitterGatewayServerMessage("TIGIMENABLE コマンドは4つの引数(ServiceServerName, ServerName, UserName, Password)が必要です。");
                return;
            }
            
            _config.IMServiceServerName = e.Message.CommandParams[0];
            _config.IMServerName = e.Message.CommandParams[1];
            _config.IMUserName = e.Message.CommandParams[2];
            _config.SetIMPassword(_password, e.Message.CommandParams[3]);
            SaveConfig();
            ConnectToIMService(true);
        }

        void MessageReceived_TIGIMDISABLE(object sender, MessageReceivedEventArgs e)
        {
            if (String.Compare(e.Message.Command, "TIGIMDISABLE", true) != 0) return;
            _config.IMServiceServerName = _config.IMServerName = _config.IMUserName = _config.IMEncryptoPassword = "";
            SaveConfig();
            DisconnectToIMService(false);
        }

        private void ConnectToIMService(Boolean initialConnect)
        {
            DisconnectToIMService(!initialConnect);
            SendTwitterGatewayServerMessage(String.Format("インスタントメッセージングサービス \"{0}\" (サーバ: {1}) にユーザ \"{2}\" でログインします。", _config.IMServerName, _config.IMServiceServerName, _config.IMUserName));

            _twitterIm = new TwitterIMService(_config.IMServiceServerName, _config.IMServerName, _config.IMUserName, _config.GetIMPassword(_password));
            _twitterIm.StatusUpdateReceived += new EventHandler<TwitterIMService.StatusUpdateReceivedEventArgs>(twitterIm_StatusUpdateReceived);
            _twitterIm.Logined += new EventHandler(twitterIm_Logined);
            _twitterIm.AuthErrored += new EventHandler(twitterIm_AuthErrored);
            _twitterIm.SocketErrorHandled += new EventHandler<TwitterIMService.ErrorEventArgs>(twitterIm_SocketErrorHandled);
            _twitterIm.Closed += new EventHandler(twitterIm_Closed);
            _twitterIm.Open();

            if (initialConnect)
            {
                _requireIMReconnect = true;
                _imReconnectCount = 0;
            }
        }

        private void DisconnectToIMService(Boolean requireIMReconnect)
        {
            if (_twitterIm != null)
            {
                //SendTwitterGatewayServerMessage("インスタントメッセージングサービスから切断します。");
                _requireIMReconnect = requireIMReconnect;
                _twitterIm.Close();
                _twitterIm = null;
            }
        }

        void twitterIm_Closed(object sender, EventArgs e)
        {
            if (_requireIMReconnect && _imReconnectCount++ < 10)
            {
                SendTwitterGatewayServerMessage(String.Format("インスタントメッセージングサービスから切断しました。再接続します({0}回目)", _imReconnectCount));
                ConnectToIMService(false);
            }
            else
            {
                SendTwitterGatewayServerMessage("インスタントメッセージングサービスから切断しました。");
            }
        }
        void twitterIm_SocketErrorHandled(object sender, TwitterIMService.ErrorEventArgs e)
        {
            if (_requireIMReconnect && _imReconnectCount++ < 10)
            {
                SendTwitterGatewayServerMessage(String.Format("インスタントメッセージングサービスの接続でエラーが発生しました: {0} / 再接続します。({1}回目)", e.Exception.Message, _imReconnectCount));
                ConnectToIMService(false);
            }
            else
            {
                SendTwitterGatewayServerMessage("インスタントメッセージングサービスの接続でエラーが発生しました: " + e.Exception.Message); 
            }
        
        }
        void twitterIm_Logined(object sender, EventArgs e)
        {
            SendTwitterGatewayServerMessage("インスタントメッセージングサービスにログインしました。");
        }
        void twitterIm_AuthErrored(object sender, EventArgs e)
        {
            SendTwitterGatewayServerMessage("インスタントメッセージングサービスのログインに失敗しました。ユーザ名とパスワードが正しくありません。");
        }
        void twitterIm_StatusUpdateReceived(object sender, TwitterIMService.StatusUpdateReceivedEventArgs e)
        {
            _isFirstTime = false; // IMが先にきてしまったらあきらめる
            _twitter.ProcessStatus(e.Status, (s) =>
            {
                Boolean friendsCheckRequired = false;
                ProcessTimelineStatus(e.Status, ref friendsCheckRequired);
            });
        }
    #endregion
    }
#endif
}
