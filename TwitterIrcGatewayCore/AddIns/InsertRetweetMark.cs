using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Misuzilla.Applications.TwitterIrcGateway.AddIns
{
    public class InsertRetweetMark : AddInBase
    {
        public override void Initialize()
        {
            Type encodingType = CurrentServer.Encoding.GetType();
            if (encodingType == typeof(UTF8Encoding) || encodingType == typeof(UTF32Encoding) || encodingType == typeof(UnicodeEncoding))
            {
                CurrentSession.PreProcessTimelineStatus += (sender, e) =>
                                                                     {
                                                                         if (e.Tweet.RetweetedStatus != null)
                                                                         {
                                                                             e.Text = String.Format("♻ RT @{0}: {1}", e.Tweet.RetweetedStatus.User.ScreenName, e.Tweet.RetweetedStatus.Text);
                                                                             e.Tweet.Entities = e.Tweet.RetweetedStatus.Entities; // 詰め替え
                                                                         }
                                                                     };
            }
        }
    }
}
