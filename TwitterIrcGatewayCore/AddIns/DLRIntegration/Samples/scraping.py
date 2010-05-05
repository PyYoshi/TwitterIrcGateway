import sys
import clr
import re
import thread
import time

import Misuzilla.Applications.TwitterIrcGateway
import Misuzilla.Applications.TwitterIrcGateway.AddIns
import Misuzilla.Applications.TwitterIrcGateway.AddIns.Console

from System import *
from System.Threading import Thread, ThreadStart
from System.Collections.Generic import *
from System.Diagnostics import Trace
from Misuzilla.Applications.TwitterIrcGateway import Status, Statuses, User, Users, Utility
from Misuzilla.Applications.TwitterIrcGateway.AddIns import IConfiguration, ConfigurationPropertyInfo
from Misuzilla.Applications.TwitterIrcGateway.AddIns.Console import ConsoleAddIn, Console, Context
from Misuzilla.Applications.TwitterIrcGateway.AddIns.DLRIntegration import DLRIntegrationAddIn, DLRBasicConfiguration, DLRContextHelper

class ScrapingContext(Context):
	def Initialize(self):
		self.scraping = Scraping.instance()
		self.config = self.scraping.config
		pass

	def GetCommands(self):
		dict = Context.GetCommands(self)
		dict["Interval"] = "取得間隔を設定します。"
		dict["Relogin"] = "再ログインします。"
		dict["Enable"] = "スクレイピングを有効にします。"
		dict["Disable"] = "スクレイピングを無効にします。"
		return dict

	def OnUninitialize(self):
		pass

	def get_Configurations(self):
		return Array[IConfiguration]([ self.config ])

	# Implementation
	def Interval(self, args):
		if String.IsNullOrEmpty(args):
			self.Console.NotifyMessage("取得間隔を指定してください。")
			return
		interval = int(args, 10)
		self.scraping.interval = interval
		self.config.SetValue("Interval", interval)
		self.Console.NotifyMessage("取得間隔を %s 秒に設定しました。" % args)
		self.scraping.start()

	def Relogin(self, args):
		self.Console.NotifyMessage("ログインしています...")
		CurrentSession.TwitterService.CookieLogin()
		self.Console.NotifyMessage("ログインしました。")

	def Enable(self, args):
		self.config.SetValue("Enable", True)
		self.scraping.enable = True
		self.scraping.start()
		self.Console.NotifyMessage("スクレイピングを有効にしました")

	def Disable(self, args):
		self.config.SetValue("Enable", False)
		self.scraping.enable = False
		self.Console.NotifyMessage("スクレイピングを無効にしました")

class Scraping(Object):
	@classmethod
	def instance(klass):
		if not hasattr(klass, 'instance_'):
			klass.instance_ = Scraping()
		return klass.instance_

	def __init__(self):
		# 普通の #Console にコンテキストを追加する
		CurrentSession.AddInManager.GetAddIn[ConsoleAddIn]().RegisterContext(DLRContextHelper.Wrap(CurrentSession, "ScrapingContext", ScrapingContext), "Scraping", "スクレイピングの設定を行うコンテキストに切り替えます")
		CurrentSession.AddInManager.GetAddIn[DLRIntegrationAddIn]().BeforeUnload += self.onBeforeUnload
		CurrentSession.PostProcessTimelineStatuses += self.onPostProcessTimelineStatuses
		self.running = False
		self.thread = None
		
		self.config = DLRBasicConfiguration(CurrentSession, "ScrapingContext", Array[ConfigurationPropertyInfo]([ConfigurationPropertyInfo("Enable", "スクレイピングを利用するかどうか", Boolean, False, None), ConfigurationPropertyInfo("Interval", "取得間隔", Int32, 30, None), ConfigurationPropertyInfo("DisableTimelineApi", "APIによるタイムライン取得を停止するかどうか", Boolean, False, None)]))

		self.interval = self.config.GetValue("Interval")
		self.enable = self.config.GetValue("Enable")

		self.re_source = re.compile(r"<span>from (.*?)</span>")
		self.re_statuses = re.compile(r"<li class=\"hentry u-.*? status.*?</li>", re.S)
		self.re_content = re.compile(r"class=\"entry-content\">(.*?)</span>")
		self.re_user = re.compile(r"class=\"tweet-url screen-name\" title=\"([^\"]+)\">(.*?)</a>")
		self.re_anchor = re.compile(r"<a href=\"(http://[^\"]*)\"[^>]*>.*?</a>")
		self.re_tag = re.compile(r"<[^>]*>")
		self.re_status_id = re.compile(r"id=\"status_(\d+)\"")
		self.doProcessStatusAction = Action[Status](self.doProcessStatus)

	def start(self):
		if not self.running:
			CurrentSession.TwitterService.CookieLogin()
			self.thread = Thread(ThreadStart(self.runProc))
			self.thread.Start()

	def runProc(self):
		self.running = True
		while self.interval > 0 and self.enable:
			try:
				self.fetchHome()
			except:
				Trace.WriteLine(sys.exc_info().ToString())
			Thread.Sleep(self.interval * 1000)
		self.running = False

	def fetchHome(self):
		home = CurrentSession.TwitterService.GETWithCookie("/home")
		statuses = self.re_statuses.findall(home)
		statuses.reverse()
		for status in statuses:
			s = Status()
			# User
			match = self.re_user.search(status)
			s.User            = User()
			s.User.Id         = 0
			s.User.Name       = match.group(1)
			s.User.ScreenName = match.group(2)
			
			# Status
			s.Source    = self.re_source.search(status).group(1)
			s.Text      = Utility.UnescapeCharReference(self.re_tag.sub(r"", self.re_anchor.sub(r"\1", self.re_content.search(status).group(1))))
			s.Id        = long(self.re_status_id.search(status).group(1), 10)
			s.CreatedAt = DateTime.Now
			
			Trace.WriteLine(s.ToString())
			CurrentSession.TwitterService.ProcessStatus(s, self.doProcessStatusAction)

	def doProcessStatus(self, s):
		CurrentSession.ProcessTimelineStatus(s, False, False)

	def onPostProcessTimelineStatuses(self, sender, e):
		if e.IsFirstTime and self.requireDisableApi():
			CurrentSession.TwitterService.Interval = 360000
			CurrentSession.TwitterService.Stop()
			CurrentSession.TwitterService.Start()

	def onBeforeUnload(self, sender, e):
		CurrentSession.AddInManager.GetAddIn[ConsoleAddIn]().UnregisterContext(DLRContextHelper.Wrap(CurrentSession, "ScrapingContext", ScrapingContext))
		CurrentSession.PostProcessTimelineStatuses -= self.onPostProcessTimelineStatuses
		self.interval = 0
		self.thread.Abort()
		self.thread.Join(5000)
		
	def requireDisableApi(self):
		return self.config.GetValue("DisableTimelineApi")

scraping = Scraping.instance()
scraping.start()
