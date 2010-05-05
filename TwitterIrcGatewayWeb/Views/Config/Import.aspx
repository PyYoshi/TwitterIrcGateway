<%@ Page Title="" Language="C#" MasterPageFile="~/Views/Shared/Site.Master" Inherits="System.Web.Mvc.ViewPage" %>

<asp:Content ID="Content1" ContentPlaceHolderID="TitleContent" runat="server">
インポート | 設定
</asp:Content>

<asp:Content ID="Content2" ContentPlaceHolderID="MainContent" runat="server">

<h2>設定のインポート</h2>
<p>以前利用していたTwitterIrcGatewayの設定ファイルをインポート出来ます。</p>
<p>注意: インポートする前にTwitterIrcGatewayに対するすべての接続を切断してください。また、アップロードすると現在の設定は失われます。</p>

<div class="blockForm">
<h3>基本設定 (Config.xml)</h3>
<% Html.BeginForm("Import", "Config", new { target = "Config" }, FormMethod.Post, new { enctype = "multipart/form-data" }); %>
ファイル: <input type="file" name="uploadFile" /><br />
<input type="submit" value="アップロード" />
<% Html.EndForm(); %>
<!--/blockForm--></div>

<div class="blockForm">
<h3>グループ設定 (Groups.xml)</h3>
<% Html.BeginForm("Import", "Config", new { target = "Groups" }, FormMethod.Post, new { enctype = "multipart/form-data" }); %>
ファイル: <input type="file" name="uploadFile" /><br />
<input type="submit" value="アップロード" />
<% Html.EndForm(); %>
<!--/blockForm--></div>

</asp:Content>
