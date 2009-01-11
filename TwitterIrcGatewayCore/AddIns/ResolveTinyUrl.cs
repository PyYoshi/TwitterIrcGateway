﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Misuzilla.Applications.TwitterIrcGateway.AddIns
{
    class ResolveTinyUrl : AddInBase
    {
        public override void Initialize()
        {
            Session.PostFilterProcessTimelineStatus += new EventHandler<TimelineStatusEventArgs>(Session_PostFilterProcessTimelineStatus);
            //Session.PreSendUpdateStatus += new EventHandler<StatusUpdateEventArgs>(Session_PreSendUpdateStatus);
        }

        //void Session_PreSendUpdateStatus(object sender, StatusUpdateEventArgs e)
        //{
        //    e.Text = Utility.UrlToTinyUrlInMessage(e.Text);
        //}
        
        void Session_PostFilterProcessTimelineStatus(object sender, TimelineStatusEventArgs e)
        {
            // TinyURL
            e.Text = (Server.ResolveTinyUrl) ? Utility.ResolveTinyUrlInMessage(e.Text) : e.Text;
        }
    }
}