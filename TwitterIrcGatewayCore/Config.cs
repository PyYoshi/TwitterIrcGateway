﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Xml;
using System.IO;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Security.Cryptography;
using Misuzilla.Applications.TwitterIrcGateway.AddIns;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    public class Config : MarshalByRefObject, IConfiguration
    {
        [Browsable(false)]
        public String IMServiceServerName { get; set; }
        [Browsable(false)]
        public String IMServerName { get; set; }
        [Browsable(false)]
        public String IMUserName { get; set; }
        [Browsable(false)]
        public String IMEncryptoPassword { get; set; }
        [Browsable(false)]
        public List<String> DisabledAddInsList { get; set; }

        [Description("TypableMapを有効化または無効化します")]
        public Boolean EnableTypableMap { get; set; }
        [Description("TypableMapのキーサイズを変更します")]
        public Int32 TypableMapKeyColorNumber { get; set; }
        [Description("TypableMapの色番号を変更します")]
        public Int32 TypableMapKeySize { get; set; }

        [Description("冗長な末尾削除を有効化または無効化します")]
        public Boolean EnableRemoveRedundantSuffix { get; set; }
        [Description("@で返信した際に最後に受信したステータスに対して返すかどうかを指定します")]
        public Boolean EnableOldStyleReply { get; set; }

        /// <summary>
        /// チェックする間隔
        /// </summary>
        [Description("チェックする間隔")]
        public Int32 Interval { get; set; }
        /// <summary>
        /// ダイレクトメッセージをチェックする間隔
        /// </summary>
        [Description("ダイレクトメッセージをチェックする間隔")]
        public Int32 IntervalDirectMessage { get; set; }
        /// <summary>
        /// Repliesをチェックするかどうか
        /// </summary>
        [Description("Repliesをチェックするかどうか")]
        public Boolean EnableRepliesCheck { get; set; }
        /// <summary>
        /// Repliesチェックする間隔
        /// </summary>
        [Description("Repliesチェックする間隔")]
        public Int32 IntervalReplies { get; set; }
        /// <summary>
        /// エラーを無視するかどうか
        /// </summary>
        [Description("エラーを無視するかどうか")]
        public Boolean IgnoreWatchError { get; set; }
        /// <summary>
        /// TinyURLを展開するかどうか
        /// </summary>
        [Description("TinyURLを展開するかどうか")]
        public Boolean ResolveTinyUrl { get; set; }
        /// <summary>
        /// 取りこぼし防止を利用するかどうか
        /// </summary>
        [Description("取りこぼし防止を利用するかどうか")]
        public Boolean EnableDropProtection { get; set; }
        /// <summary>
        /// ステータスを更新したときにトピックを変更するかどうか
        /// </summary>
        [Description("ステータスを更新したときにトピックを変更するかどうか")]
        public Boolean SetTopicOnStatusChanged { get; set; }
        /// <summary>
        /// トレースを有効にするかどうか
        /// </summary>
        [Description("トレースを有効にするかどうか")]
        public Boolean EnableTrace { get; set; }
        /// <summary>
        /// Twitterのステータスが流れるチャンネル名
        /// </summary>
        [Description("Twitterのステータスが流れるチャンネル名")]
        public String ChannelName { get; set; }
        /// <summary>
        /// ユーザ一覧を取得するかどうか
        /// </summary>
        [Description("ユーザ一覧を取得するかどうか")]
        public Boolean DisableUserList { get; set; }
        /// <summary>
        /// アップデートをすべてのチャンネルに投げるかどうか
        /// </summary>
        [Description("アップデートをすべてのチャンネルに投げるかどうか")]
        public Boolean BroadcastUpdate { get; set; }
        /// <summary>
        /// クライアントにメッセージを送信するときのウェイト
        /// </summary>
        [Description("クライアントにメッセージを送信するときのウェイト")]
        public Int32 ClientMessageWait { get; set; }
        /// <summary>
        /// アップデートをすべてのチャンネルに投げるときNOTICEにするかどうか
        /// </summary>
        [Description("アップデートをすべてのチャンネルに投げるときNOTICEにするかどうか")]
        public Boolean BroadcastUpdateMessageIsNotice { get; set; }
        /// <summary>
        /// データの取得にPOSTメソッドを利用するかどうか
        /// </summary>
        [Description("データの取得にPOSTメソッドを利用するかどうか")]
        public Boolean POSTFetchMode { get; set; }

        /// <summary>
        /// デフォルトの設定
        /// </summary>
        public static Config Default = new Config();
        
        public Config()
        {
            EnableTypableMap = false;
            TypableMapKeyColorNumber = 14;
            TypableMapKeySize = 2;
            EnableRemoveRedundantSuffix = false;
            DisabledAddInsList = new List<string>();
            EnableOldStyleReply = false;

            if (Default != null)
            {
                Interval = Default.Interval;
                IntervalDirectMessage = Default.IntervalDirectMessage;
                EnableRepliesCheck = Default.EnableRepliesCheck;
                IntervalReplies = Default.IntervalReplies;
                IgnoreWatchError = Default.IgnoreWatchError;
                ResolveTinyUrl = Default.ResolveTinyUrl;
                EnableDropProtection = Default.EnableDropProtection;
                SetTopicOnStatusChanged = Default.SetTopicOnStatusChanged;
                EnableTrace = Default.EnableTrace;
                ChannelName = Default.ChannelName;
                DisableUserList = Default.DisableUserList;
                BroadcastUpdate = Default.BroadcastUpdate;
                ClientMessageWait = Default.ClientMessageWait;
                BroadcastUpdateMessageIsNotice = Default.BroadcastUpdateMessageIsNotice;
                POSTFetchMode = Default.POSTFetchMode;
            }
        }

        #region XML Serialize
        private static Object _syncObject = new object();
        private static XmlSerializer _serializer = null;
        static Config()
        {
            lock (_syncObject)
            {
                if (_serializer == null)
                {
                    _serializer = new XmlSerializer(typeof(Config));
                }
            }
        }
        public static XmlSerializer Serializer
        {
            get
            {
                return _serializer;
            }
        }

        public void Serialize(Stream stream)
        {
            using (XmlTextWriter xmlTextWriter = new XmlTextWriter(stream, Encoding.UTF8))
            {
                _serializer.Serialize(xmlTextWriter, this);
            }
        }

        public static Config Deserialize(Stream stream)
        {
            return _serializer.Deserialize(stream) as Config;
        }
        #endregion

        public String GetIMPassword(String key)
        {
            StringBuilder sb = new StringBuilder();
            String passwordDecoded = Encoding.UTF8.GetString(Convert.FromBase64String(IMEncryptoPassword));
            for (var i = 0; i < passwordDecoded.Length; i++)
            {
                sb.Append((Char)(passwordDecoded[i] ^ key[i % key.Length]));
            }
            return sb.ToString();
        }

        public void SetIMPassword(String key, String password)
        {
            StringBuilder sb = new StringBuilder();
            for (var i = 0; i < password.Length; i++)
            {
                sb.Append((Char)(password[i] ^ key[i % key.Length]));
            }
            IMEncryptoPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static Config Load(String path)
        {
            // group 読み取り
            if (File.Exists(path))
            {
                Trace.WriteLine(String.Format("Load Config: {0}", path));
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open))
                    {
                        try
                        {
                            Config config = Config.Deserialize(fs);
                            if (config != null)
                                return config;
                        }
                        catch (XmlException xe) { Trace.WriteLine(xe.Message); }
                        catch (InvalidOperationException ioe) { Trace.WriteLine(ioe.Message); }
                    }
                }
                catch (IOException ie)
                {
                    Trace.WriteLine(ie.Message);
                    throw;
                }
            }
            return new Config();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        public void Save(String path)
        {
            Trace.WriteLine(String.Format("Save Config: {0}", path));
            try
            {
                String dir = Path.GetDirectoryName(path);
                Directory.CreateDirectory(dir);
                using (FileStream fs = new FileStream(path, FileMode.Create))
                {
                    try
                    {
                        this.Serialize(fs);
                    }
                    catch (XmlException xe) { Trace.WriteLine(xe.Message); }
                    catch (InvalidOperationException ioe) { Trace.WriteLine(ioe.Message); }
                }
            }
            catch (IOException ie)
            {
                Trace.WriteLine(ie.Message);
                throw;
            }
        }
    }
}
