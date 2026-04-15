<%@ Page Language="C#" %>
<%@ Import Namespace="System" %>
<%@ Import Namespace="System.Web" %>
<%@ Import Namespace="System.Web.Hosting" %>
<%@ Import Namespace="WebApplication" %>

<script runat="server">
    protected void Page_Load(object sender, EventArgs e)
    {
        UpdateStatus();
    }

    protected void btnRegister_Click(object sender, EventArgs e)
    {
        if (!LabVppState.Registered)
        {
            HostingEnvironment.RegisterVirtualPathProvider(new LabVirtualPathProvider());
            LabVppState.Registered = true;
        }
        LabVppState.Active = true;
        UpdateStatus();
    }

    protected void btnDeactivate_Click(object sender, EventArgs e)
    {
        LabVppState.DeactivateAndUnload();
        // AppDomain is recycling — the response will redirect to the fresh instance.
        Response.Redirect(Request.RawUrl, true);
    }

    protected void btnFlushCache_Click(object sender, EventArgs e)
    {
        lblCacheResult.Text = HttpUtility.HtmlEncode(LabVppState.FlushDiskCompilationCache());
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var appPath = HttpRuntime.AppDomainAppVirtualPath.TrimEnd('/');
        var vppUrl = appPath + "/googlecheck.aspx?token=" + HttpUtility.UrlEncode(LabVppState.Token);

        lblStatus.Text = "VPP registered: " + LabVppState.Registered + "<br/>" +
                         "VPP active: " + LabVppState.Active + "<br/>" +
                         "Token: " + HttpUtility.HtmlEncode(LabVppState.Token) + "<br/>" +
                         "Virtual URL: <a href=\"" + vppUrl + "\">" +
                         HttpUtility.HtmlEncode(vppUrl) + "</a><br/>" +
                         "CodegenDir: <code>" + HttpUtility.HtmlEncode(HttpRuntime.CodegenDir) + "</code>";
    }
</script>

<!DOCTYPE html>
<html>
<head runat="server">
    <title>VPP Lab</title>
</head>
<body>
<form id="form1" runat="server">
    <h2>VirtualPathProvider Lab</h2>

    <asp:Button ID="btnRegister" runat="server" Text="Register VPP (and activate)" OnClick="btnRegister_Click" />
    <asp:Button ID="btnDeactivate" runat="server" Text="Deactivate (AND unregister)" OnClick="btnDeactivate_Click" />
    <asp:Button ID="btnFlushCache" runat="server" Text="Flush disk compilation cache" OnClick="btnFlushCache_Click" />

    <hr />
    <asp:Label ID="lblStatus" runat="server" />
    <br />
    <asp:Label ID="lblCacheResult" runat="server" ForeColor="Green" />
</form>
</body>
</html>
