using System;
using System.Collections.Generic;
using System.Text;

namespace Misuzilla.Applications.TwitterIrcGateway.AddIns
{
    class ResolveShortUrlServices : AddInBase
    {
        public override void Initialize()
        {
            CurrentSession.PreFilterProcessTimelineStatus += new EventHandler<TimelineStatusEventArgs>(Session_PreFilterProcessTimelineStatus);
            //Session.PreSendUpdateStatus += new EventHandler<StatusUpdateEventArgs>(Session_PreSendUpdateStatus);
        }

        //void Session_PreSendUpdateStatus(object sender, StatusUpdateEventArgs e)
        //{
        //    e.Text = Utility.UrlToTinyUrlInMessage(e.Text);
        //}
        
        void Session_PreFilterProcessTimelineStatus(object sender, TimelineStatusEventArgs e)
        {
            // TinyURL
            e.Text = (CurrentSession.Config.ResolveTinyUrl) ? Utility.ResolveShortUrlInMessage(Utility.ResolveTinyUrlInMessage(e.Text))
                                                            : e.Text;
        }
    }
}
