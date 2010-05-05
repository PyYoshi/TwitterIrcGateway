<%@ Page Title="" Language="C#" MasterPageFile="~/Views/Shared/Site.Master" Inherits="System.Web.Mvc.ViewPage" %>

<asp:Content ID="Content1" ContentPlaceHolderID="TitleContent" runat="server">
エクスポート | 設定
</asp:Content>

<asp:Content ID="Content2" ContentPlaceHolderID="MainContent" runat="server">

<h2>設定のエクスポート</h2>

<ul>
<li><a href="<%= Url.Action("Export", new { target = "Config" }) %>">基本設定(Config.xml)</a></li>
<li><a href="<%= Url.Action("Export", new { target = "Groups" }) %>">グループ設定(Groups.xml)</a></li>
</ul>

</asp:Content>
