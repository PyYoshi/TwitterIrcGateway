#
# ���b�Z�[�W����URL��TinyURL�ŕϊ����܂�
# $Id$
#
import clr
from Misuzilla.Applications.TwitterIrcGateway import Status, Statuses, User, Users, Utility
from Misuzilla.Applications.TwitterIrcGateway.AddIns import IConfiguration
from Misuzilla.Applications.TwitterIrcGateway.AddIns.Console import ConsoleAddIn, Console, Context
from Misuzilla.Applications.TwitterIrcGateway.AddIns.DLRIntegration import DLRIntegrationAddIn, DLRBasicConfiguration, DLRContextHelper

def OnPreSendUpdateStatus(sender, e):
	e.Text = Utility.UrlToTinyUrlInMessage(e.Text)

# ��Еt��
def OnBeforeUnload(sender, e):
	Session.PreSendUpdateStatus -= OnPreSendUpdateStatus

Session.PreSendUpdateStatus += OnPreSendUpdateStatus
Session.AddInManager.GetAddIn[DLRIntegrationAddIn]().BeforeUnload += OnBeforeUnload