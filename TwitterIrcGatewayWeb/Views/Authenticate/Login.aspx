<%@ Page Title="" Language="C#" MasterPageFile="~/Views/Shared/Site.Master" Inherits="System.Web.Mvc.ViewPage" %>

<asp:Content ID="Content1" ContentPlaceHolderID="TitleContent" runat="server">
	Login
</asp:Content>

<asp:Content ID="Content2" ContentPlaceHolderID="MainContent" runat="server">

    <h2>ログイン</h2>
    <p><a href="<%= Url.Action("RequestToken", new { BackUrl = ViewData["BackUrl"] }) %>">Twitterのサイトでログインします。</a></p>

</asp:Content>
