<%@ Page Title="" Language="C#" MasterPageFile="~/Views/Shared/Site.Master" Inherits="System.Web.Mvc.ViewPage" %>

<asp:Content ID="Content1" ContentPlaceHolderID="TitleContent" runat="server">
	設定
</asp:Content>

<asp:Content ID="Content2" ContentPlaceHolderID="MainContent" runat="server">

    <h2>設定</h2>
    <ul>
    <li><a href="<%= Url.Action("Import") %>">設定のインポート</a></li>
    <li><a href="<%= Url.Action("Export") %>">設定のエクスポート</a></li>
    </ul>

</asp:Content>
