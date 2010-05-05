using System;
using System.Collections.Generic;
using System.Text;

namespace Misuzilla.Applications.TwitterIrcGateway.AddIns
{
    class ResolveShortUrlServices : AddInBase
    {
        public override void Initialize()
        {
            CurrentSession.PostFilterProcessTimelineStatus += new EventHandler<TimelineStatusEventArgs>(Session_PostFilterProcessTimelineStatus);
            //Session.PreSendUpdateStatus += new EventHandler<StatusUpdateEventArgs>(Session_PreSendUpdateStatus);
        }

        //void Session_PreSendUpdateStatus(object sender, StatusUpdateEventArgs e)
        //{
        //    e.Text = Utility.UrlToTinyUrlInMessage(e.Text);
        //}
        
        void Session_PostFilterProcessTimelineStatus(object sender, TimelineStatusEventArgs e)
        {
            // TinyURL
            e.Text = (CurrentSession.Config.ResolveTinyUrl) ? Utility.ResolveBitlyInMessage(Utility.ResolveTinyUrlInMessage(e.Text))
                                                            : e.Text;
        }
    }
}
