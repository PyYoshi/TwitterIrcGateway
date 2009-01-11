﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Misuzilla.Applications.TwitterIrcGateway.Filter;
using Misuzilla.Net.Irc;

namespace Misuzilla.Applications.TwitterIrcGateway.AddIns.Console
{
    /// <summary>
    /// 
    /// </summary>
    public class RootContext : Context
    {
        [Description("Twitter 検索を利用して検索します")]
        public void Search(String keywords)
        {
            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load("http://pcod.no-ip.org/yats/search?rss&query=" + Utility.UrlEncode(keywords));
                XmlNamespaceManager nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
                nsMgr.AddNamespace("a", "http://www.w3.org/2005/Atom");

                XmlNodeList entries = xmlDoc.SelectNodes("//a:entry", nsMgr);

                for (var i = (Math.Min(entries.Count, ConsoleAddIn.Config.SearchCount)); i > 0; i--)
                {
                    // 後ろから取っていく
                    XmlElement entryE = entries[i - 1] as XmlElement;
                    String screenName = entryE.SelectSingleNode("a:title/text()", nsMgr).Value;
                    String body = entryE.SelectSingleNode("a:summary/text()", nsMgr).Value;
                    String link = entryE.SelectSingleNode("a:link/@href", nsMgr).Value;
                    DateTime updated = DateTime.Parse(entryE.SelectSingleNode("a:updated/text()", nsMgr).Value);

                    body = Regex.Replace(body, "^@[^ ]+ : ", "");

                    StringBuilder sb = new StringBuilder();
                    sb.Append(updated.ToString("HH:mm")).Append(": ").Append(body);
                    if (ConsoleAddIn.Config.ShowPermalinkAfterStatus)
                        sb.Append(" ").Append(link);

                    Session.Send(new NoticeMessage(ConsoleAddIn.ConsoleChannelName, sb.ToString()) { SenderHost = Server.ServerName, SenderNick = screenName });

                }
            }
            catch (WebException we)
            {
                ConsoleAddIn.NotifyMessage("Twitter 検索へのリクエスト中にエラーが発生しました:");
                ConsoleAddIn.NotifyMessage(we.Message);
            }
        }

        [Description("指定したユーザのタイムラインを取得します")]
        public void Timeline(params String[] screenNames)
        {
            List<Status> statuses = new List<Status>();
            foreach (var screenName in screenNames)
            {
                try
                {
                    var retStatuses = Session.TwitterService.GetTimelineByScreenName(screenName, new DateTime(), ConsoleAddIn.Config.SearchCount);
                    statuses.AddRange(retStatuses.Status);
                }
                catch (TwitterServiceException te)
                {
                    ConsoleAddIn.NotifyMessage(String.Format("ユーザ {0} のタイムラインを取得中にエラーが発生しました:", screenName));
                    ConsoleAddIn.NotifyMessage(te.Message);
                }
                catch (WebException we)
                {
                    ConsoleAddIn.NotifyMessage(String.Format("ユーザ {0} のタイムラインを取得中にエラーが発生しました:", screenName));
                    ConsoleAddIn.NotifyMessage(we.Message);
                }
            }

            statuses.Sort((a, b) => ((a.Id == b.Id) ? 0 : ((a.Id > b.Id) ? 1 : -1)));
            foreach (var status in statuses)
            {
                Session.Send(new NoticeMessage(ConsoleAddIn.ConsoleChannelName, String.Format("{0}: {1}", status.CreatedAt.ToString("HH:mm"), status.Text)) { SenderHost = Server.ServerName, SenderNick = status.User.ScreenName });
            }
        }

        [Description("指定したユーザを follow します")]
        public void Follow(params String[] screenNames)
        {
            FollowOrRemove(true, screenNames);
        }

        [Description("指定したユーザを remove します")]
        public void Remove(params String[] screenNames)
        {
            FollowOrRemove(false, screenNames);
        }

        [Description("フィルタの設定を行うコンテキストに切り替えます")]
        public void Filter()
        {
            ConsoleAddIn.PushContext(Context.GetContext<FilterContext>(Server, Session));
        }

        [Description("設定を行うコンテキストに切り替えます")]
        public void Config()
        {
            ConsoleAddIn.PushContext(Context.GetContext<ConfigContext>(Server, Session));
        }

        //
        [Browsable(false)]
        private void FollowOrRemove(Boolean follow, String[] screenNames)
        {
            String action = follow ? "follow" : "remove";

            foreach (var screenName in screenNames)
            {
                try
                {
                    var user = follow
                                   ? Session.TwitterService.CreateFriendship(screenName)
                                   : Session.TwitterService.DestroyFriendship(screenName);
                    ConsoleAddIn.NotifyMessage(String.Format("ユーザ {0} を {1} しました。", screenName, action));
                }
                catch (TwitterServiceException te)
                {
                    ConsoleAddIn.NotifyMessage(String.Format("ユーザ {0} を {1} する際にエラーが発生しました:", screenName, action));
                    ConsoleAddIn.NotifyMessage(te.Message);
                }
                catch (WebException we)
                {
                    ConsoleAddIn.NotifyMessage(String.Format("ユーザ {0} を {1} する際にエラーが発生しました:", screenName, action));
                    ConsoleAddIn.NotifyMessage(we.Message);
                }
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class FilterContext : Context
    {
        [Description("存在するフィルタをすべて表示します")]
        public void List()
        {
            for (var i = 0; i < Session.Filters.Items.Length; i++)
            {
                FilterItem filter = Session.Filters.Items[i];
                ConsoleAddIn.NotifyMessage(String.Format("{0}: {1}", i, filter.ToString()));
            }
        }

        [Description("指定したフィルタを有効化します")]
        public void Enable(String args)
        {
            SwitchEnable(args, true);
        }

        [Description("指定したフィルタを無効化します")]
        public void Disable(String args)
        {
            SwitchEnable(args, false);
        }

        private void SwitchEnable(String args, Boolean enable)
        {
            Int32 index;
            FilterItem[] items = Session.Filters.Items;
            if (Int32.TryParse(args, out index))
            {
                if (index < items.Length && index > -1)
                {
                    items[index].Enabled = enable;
                    Session.SaveFilters();
                    ConsoleAddIn.NotifyMessage(String.Format("フィルタ {0} を{1}化しました。", items[index], (enable ? "有効" : "無効")));
                }
                else
                {
                    ConsoleAddIn.NotifyMessage("存在しないフィルタが指定されました。");
                }
            }
            else
            {
                ConsoleAddIn.NotifyMessage("フィルタの指定が正しくありません。");
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class ConfigContext : Context
    {
        [Description("Search コマンドでの検索時の表示件数を指定します")]
        public void SearchCount(Int32 count)
        {
            ConsoleAddIn.Config.SearchCount = count;
            ConsoleAddIn.NotifyMessage("SearchCount = " + count);
            Session.AddInManager.SaveConfig(ConsoleAddIn.Config);
        }

        [Description("Timeline コマンドでのタイムライン取得時の各タイムラインごとの表示件数を指定します")]
        public void TimelineCount(Int32 count)
        {
            ConsoleAddIn.Config.TimelineCount = count;
            ConsoleAddIn.NotifyMessage("TimelineCount = " + count);
            Session.AddInManager.SaveConfig(ConsoleAddIn.Config);
        }

        [Description("Search コマンドでの検索時のステータスの後ろにURLをつけるかどうかを指定します")]
        public void ShowPermalinkAfterStatus(Boolean value)
        {
            ConsoleAddIn.Config.ShowPermalinkAfterStatus = value;
            ConsoleAddIn.NotifyMessage("ShowPermalinkAfterStatus = " + value);
            Session.AddInManager.SaveConfig(ConsoleAddIn.Config);
        }
    }
}