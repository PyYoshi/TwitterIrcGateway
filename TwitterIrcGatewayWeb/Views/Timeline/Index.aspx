<%@ Page Title="" Language="C#" MasterPageFile="~/Views/Shared/Site.Master" Inherits="System.Web.Mvc.ViewPage<IQueryable<IGrouping<Misuzilla.Applications.TwitterIrcGateway.AddIns.SqlServerDataStore.Group, Timeline>>>" %>
<%@ Import Namespace="Misuzilla.Applications.TwitterIrcGateway.AddIns.SqlServerDataStore"%>

<asp:Content ID="Content1" ContentPlaceHolderID="TitleContent" runat="server">
	Timeline
</asp:Content>

<asp:Content ID="Content2" ContentPlaceHolderID="MainContent" runat="server">

<h2>Timeline</h2>


<% foreach (var group in Model){ %>
<div class="group">
<h3><%= Html.Encode(group.Key.Name) %></h3>
<ul>
<% foreach (var timelineItem in group.Take(50)) { if (timelineItem.Status == null) continue; %>
<li><%= Html.Encode(timelineItem.Status.ScreenName) %>: <%= Html.Encode(timelineItem.Status.Text) %></li>
<% } %>
</ul>
<!--/group--></div>
<% } %>


</asp:Content>

