using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Misuzilla.Applications.TwitterIrcGateway.AddIns.Console
{
    /// <summary>
    /// 
    /// </summary>
    [Browsable(false)]
    public class RootContext : Context
    {
        public override ICollection<ContextInfo> Contexts { get { return Console.Contexts.Values; } }

        [Description("Twitter 検索を利用して検索します")]
        public void Search(String keywords)
        {
            /*
             * TODO: Search APIを使用するように変更
             */
            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                using (WebClient webClient = new WebClient())
                {
                    webClient.Headers["Accept"] = "application/xml, text/xml";
                    webClient.Headers["User-Agent"] = "TwitterIrcGateway/2.0";
                    webClient.Encoding = Encoding.UTF8;
                    String xmlString = webClient.DownloadString("http://pcod.no-ip.org/yats/search?rss&query=" + Utility.UrlEncode(keywords));
                    xmlDoc.LoadXml(xmlString);
                }
                XmlNamespaceManager nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
                nsMgr.AddNamespace("a", "http://www.w3.org/2005/Atom");

                XmlNodeList entries = xmlDoc.SelectNodes("//a:entry", nsMgr);

                if (entries.Count == 0)
                {
                    Console.NotifyMessage("検索結果は見つかりませんでした。");
                    return;
                }

                for (var i = (Math.Min(entries.Count, Console.Config.SearchCount)); i > 0; i--)
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
                    if (Console.Config.ShowPermalinkAfterStatus)
                        sb.Append(" ").Append(link);

                    Console.NotifyMessage(screenName, sb.ToString());
                }
            }
            catch (WebException we)
            {
                Console.NotifyMessage("Twitter 検索へのリクエスト中にエラーが発生しました:");
                Console.NotifyMessage(we.Message);
            }
        }

        [Description("指定したユーザのタイムラインを取得します")]
        public void Timeline(params String[] screenNames)
        {
            List<Tweet> tweets = new List<Tweet>();
            foreach (var screenName in screenNames)
            {
                try
                {
                    List<Tweet> retTweets = CurrentSession.TwitterService.GetTimelineByScreenName(screenName, 0, Console.Config.SearchCount);
                    tweets.AddRange(retTweets);
                }
                catch (TwitterServiceException te)
                {
                    Console.NotifyMessage(String.Format("ユーザ {0} のタイムラインを取得中にエラーが発生しました:", screenName));
                    Console.NotifyMessage(te.Message);
                }
                catch (WebException we)
                {
                    Console.NotifyMessage(String.Format("ユーザ {0} のタイムラインを取得中にエラーが発生しました:", screenName));
                    Console.NotifyMessage(we.Message);
                }
            }

            ShowStatuses(tweets);
        }

        [Description("指定したユーザの Favorites を取得します")]
        public void Favorites(params String[] screenNames)
        {
            List<Tweet> tweets = new List<Tweet>();
            foreach (var screenName in screenNames)
            {
                try
                {
                    List<Tweet> retTweets = CurrentSession.TwitterService.GetTimelineByScreenName(screenName, 0, Console.Config.SearchCount);
                    tweets.AddRange(retTweets);
                    if (tweets.Count > Console.Config.FavoritesCount)
                        tweets.RemoveRange(10, tweets.Count - 10);
                }
                catch (TwitterServiceException te)
                {
                    Console.NotifyMessage(String.Format("ユーザ {0} の Favorites を取得中にエラーが発生しました:", screenName));
                    Console.NotifyMessage(te.Message);
                }
                catch (WebException we)
                {
                    Console.NotifyMessage(String.Format("ユーザ {0} の Favorites を取得中にエラーが発生しました:", screenName));
                    Console.NotifyMessage(we.Message);
                }
            }

            ShowStatuses(tweets);
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

        [Description("指定したユーザを block します")]
        public void Block(params String[] screenNames)
        {
            BlockOrUnblock(true, screenNames);
        }

        [Description("指定したユーザを unblock します")]
        public void Unblock(params String[] screenNames)
        {
            BlockOrUnblock(false, screenNames);
        }

        /*
         * TODO: 以下のContextを追加
         *  ・スパム報告/スパム報告取り消し
         */

        [Browsable(false)]
        private void FollowOrRemove(Boolean follow, String[] screenNames)
        {
            String action = follow ? "follow" : "remove";

            foreach (var screenName in screenNames)
            {
                try
                {
                    var user = follow
                                   ? CurrentSession.TwitterService.CreateFriendship(screenName)
                                   : CurrentSession.TwitterService.DestroyFriendship(screenName);
                    Console.NotifyMessage(String.Format("ユーザ {0} を {1} しました。", screenName, action));
                }
                catch (TwitterServiceException te)
                {
                    Console.NotifyMessage(String.Format("ユーザ {0} を {1} する際にエラーが発生しました:", screenName, action));
                    Console.NotifyMessage(te.Message);
                }
                catch (WebException we)
                {
                    Console.NotifyMessage(String.Format("ユーザ {0} を {1} する際にエラーが発生しました:", screenName, action));
                    Console.NotifyMessage(we.Message);
                }
            }
        }
        [Browsable(false)]
        private void BlockOrUnblock(Boolean block, String[] screenNames)
        {
            String action = block ? "block" : "unblock";

            foreach (var screenName in screenNames)
            {
                try
                {
                    var user = block
                                   ? CurrentSession.TwitterService.CreateBlock(screenName)
                                   : CurrentSession.TwitterService.DestroyBlock(screenName);
                    Console.NotifyMessage(String.Format("ユーザ {0} を {1} しました。", screenName, action));
                }
                catch (TwitterServiceException te)
                {
                    Console.NotifyMessage(String.Format("ユーザ {0} を {1} する際にエラーが発生しました:", screenName, action));
                    Console.NotifyMessage(te.Message);
                }
                catch (WebException we)
                {
                    Console.NotifyMessage(String.Format("ユーザ {0} を {1} する際にエラーが発生しました:", screenName, action));
                    Console.NotifyMessage(we.Message);
                }
            }
        }

        private void ShowStatuses(List<Tweet> tweets)
        {
            tweets.Sort((a, b) => ((a.Id == b.Id) ? 0 : ((a.Id > b.Id) ? 1 : -1)));
            foreach (Tweet tweet in tweets)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("{0}: {1}", tweet.CreatedAt.ToString("HH:mm"), tweet.Text);
                if (Console.Config.ShowPermalinkAfterStatus)
                    sb.AppendFormat(" http://twitter.com/{0}/status/{1}", tweet.User.ScreenName, tweet.Id);

                Console.NotifyMessage(tweet.User.ScreenName, sb.ToString());
            }
        }
    }
}
