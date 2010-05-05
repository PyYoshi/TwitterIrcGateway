using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Linq;
using Misuzilla.Applications.TwitterIrcGateway.AddIns.Console;
#if HOSTING
using Misuzilla.Applications.TwitterIrcGateway.AddIns.SqlServerDataStore;
#endif

namespace Misuzilla.Applications.TwitterIrcGateway.AddIns
{
    [Description("片思いチェックの設定を行うコンテキストに切り替えます")]
    public class RevealOnewayFollowContext : Context
    {
        public override IConfiguration[] Configurations { get { return new IConfiguration[]{ CurrentSession.AddInManager.GetAddIn<RevealOnewayFollow>().Config }; } }

        protected override void OnConfigurationChanged(IConfiguration config, MemberInfo memberInfo, object value)
        {
            CurrentSession.AddInManager.SaveConfig(config);
        }
        
        [Description("片思いチェックで利用しているFollowされているユーザーのリストを更新します。")]
        public void UpdateFollowerIds()
        {
            RevealOnewayFollow addIn = Session.AddInManager.GetAddIn<RevealOnewayFollow>();
            Console.NotifyMessage("Follower リストを更新しています。");
            addIn.UpdateFollowerIds();
            Console.NotifyMessage("Follower リストを更新しました。現在、" + addIn.FollowerIds.Count.ToString() + "人のユーザーに Follow されています。");
        }
    }
    
    public class RevealOnewayFollowConfig : IConfiguration
    {
        [Description("片思い表示を有効にするかどうかを取得・設定します。")]
        public Boolean Enable { get; set; }
    }
    
    public class RevealOnewayFollow : AddInBase
    {
        private List<Int32> _followerIds;
        internal List<Int32> FollowerIds { get { return _followerIds; } }

        public RevealOnewayFollowConfig Config { get; private set; }

        public override void Initialize()
        {
            Config = CurrentSession.AddInManager.GetConfig<RevealOnewayFollowConfig>();
            Session.AddInsLoadCompleted += (sender, e) =>
                                               {
                                                   Session.AddInManager.GetAddIn<ConsoleAddIn>().RegisterContext<RevealOnewayFollowContext>();
                                                   Session.PreSendMessageTimelineStatus += new EventHandler<TimelineStatusEventArgs>(Session_PreSendMessageTimelineStatus);
                                               };
        }

        void Session_PreSendMessageTimelineStatus(object sender, TimelineStatusEventArgs e)
        {
            if (Config.Enable && (_followerIds != null || UpdateFollowerIds()))
            {
                Int32 uid = e.Status.User.Id;
                if (uid == 0)
                {
                    // Follower から探してみる
                    User user = CurrentSession.FollowingUsers.FirstOrDefault(u => u.ScreenName == e.Status.User.ScreenName);
                    if (user != null && user.Id != 0)
                    {
                        uid = user.Id;
                    }
#if HOSTING
                    // 見つからなかったら更にSQL Server
                    if (uid == 0)
                    {
                        // SQL Serverから探してくる
                        using (TwitterIrcGatewayDataContext ctx = new TwitterIrcGatewayDataContext())
                        {
                            var dbUser =
                                ctx.User.Where(u => u.ScreenName.ToLower() == e.Status.User.ScreenName.ToLower())
                                        .FirstOrDefault();

                            if (dbUser != null && dbUser.Id != 0)
                                uid = dbUser.Id;
                        }
                    }
#endif
                }
                if (uid != Session.TwitterUser.Id && _followerIds.BinarySearch(uid) < 0)
                {
                    e.Text += " (片思い)";
                }
            }
        }

        internal Boolean UpdateFollowerIds()
        {
             if (Session.TwitterUser == null)
                 return false;

            return Session.RunCheck(() =>
                                 {
                                     try
                                     {
                                         String idsXml = Session.TwitterService.GET("/followers/ids/" + Session.TwitterUser.Id + ".xml");
                                         XmlDocument xmlDoc = new XmlDocument();
                                         xmlDoc.LoadXml(idsXml);

                                         List<Int32> followerIds = new List<Int32>();
                                         foreach (XmlElement E in xmlDoc.GetElementsByTagName("id"))
                                         {
                                             followerIds.Add(Int32.Parse(E.InnerText));
                                         }
                                         followerIds.Sort();
                                         _followerIds = followerIds;
                                         CurrentSession.Logger.Information("Followers: "+_followerIds.Count.ToString());
                                     }
                                     catch (XmlException ex)
                                     {
                                         Session.SendTwitterGatewayServerMessage("エラー: Follower リストを取得時にエラーが発生しました。("+ex.Message+")");
                                     }
                                 });
        }
    }
}
