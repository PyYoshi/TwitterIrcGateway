<%@ Control Language="C#" Inherits="System.Web.Mvc.ViewUserControl" %>
<%
    if (Request.IsAuthenticated) {
%>
        Welcome <b><%= Html.Encode(Page.User.Identity.Name) %></b>!
        [ <%= Html.ActionLink("ログアウト", "Logout", "Authenticate") %> ]
<%
    }
    else {
%> 
        [ <%= Html.ActionLink("Twitterでログイン", "Login", "Authenticate") %> ]
<%
    }
%>
