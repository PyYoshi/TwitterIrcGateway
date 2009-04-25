﻿using System;
using System.Collections.Generic;
using System.Text;
using Misuzilla.Applications.TwitterIrcGateway.AddIns.TypableMap;

namespace Misuzilla.Applications.TwitterIrcGateway.AddIns
{
    public class TypableMapSupport : AddInBase
    {
        private TypableMapCommandProcessor _typableMapCommands;
        public TypableMapCommandProcessor TypableMapCommands { get { return _typableMapCommands; } }
        
        public override void Initialize()
        {
            CurrentSession.UpdateStatusRequestReceived += new EventHandler<StatusUpdateEventArgs>(Session_UpdateStatusRequestReceived);
            CurrentSession.PreSendMessageTimelineStatus += new EventHandler<TimelineStatusEventArgs>(Session_PreSendMessageTimelineStatus);
            CurrentSession.ConfigChanged += new EventHandler<EventArgs>(Session_ConfigChanged);

            if (CurrentSession.Config.EnableTypableMap)
                _typableMapCommands = new TypableMapCommandProcessor(CurrentSession.TwitterService, CurrentSession, CurrentSession.Config.TypableMapKeySize);
        }

        void Session_PreSendMessageTimelineStatus(object sender, TimelineStatusEventArgs e)
        {
            // TypableMap
            if (CurrentSession.Config.EnableTypableMap)
            {
                String typableMapId = _typableMapCommands.TypableMap.Add(e.Status);
                // TypableMapKeyColorNumber = -1 の場合には色がつかなくなる
                if (CurrentSession.Config.TypableMapKeyColorNumber < 0)
                    e.Text = String.Format("{0} ({1})", e.Text, typableMapId);
                else
                    e.Text = String.Format("{0} \x0003{1}({2})", e.Text, CurrentSession.Config.TypableMapKeyColorNumber, typableMapId);
            }
        }

        void Session_UpdateStatusRequestReceived(object sender, StatusUpdateEventArgs e)
        {
            // Typable Map コマンド?
            if (CurrentSession.Config.EnableTypableMap)
            {
                if (_typableMapCommands.Process(e.ReceivedMessage))
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        void Session_ConfigChanged(object sender, EventArgs e)
        {
            if (CurrentSession.Config.EnableTypableMap)
            {
                if (_typableMapCommands == null)
                    _typableMapCommands = new TypableMapCommandProcessor(CurrentSession.TwitterService, CurrentSession, CurrentSession.Config.TypableMapKeySize);
                if (_typableMapCommands.TypableMapKeySize != CurrentSession.Config.TypableMapKeySize)
                    _typableMapCommands.TypableMapKeySize = CurrentSession.Config.TypableMapKeySize;
            }
            else
            {
                _typableMapCommands = null;
            }
        }
    }
}
