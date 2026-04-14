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
        LabVppState.Active = false;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        lblStatus.Text = "VPP registered: " + LabVppState.Registered + "<br/>" +
                         "VPP active: " + LabVppState.Active + "<br/>" +
                         "Token: " + HttpUtility.HtmlEncode(LabVppState.Token) + "<br/>" +
                         "Virtual URL: <a href=\"/vpp/googlecheck.aspx?token=" +
                         HttpUtility.UrlEncode(LabVppState.Token) + "\">/vpp/googlecheck.aspx?token=" +
                         HttpUtility.HtmlEncode(LabVppState.Token) + "</a>";
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
    <asp:Button ID="btnDeactivate" runat="server" Text="Deactivate (no unregister)" OnClick="btnDeactivate_Click" />

    <hr />
    <asp:Label ID="lblStatus" runat="server" />
</form>
</body>
</html>
