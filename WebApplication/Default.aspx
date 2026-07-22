<%@ Page Language="C#" %> <%@ Import Namespace="System" %> <%@ Import
Namespace="System.Diagnostics" %> <%@ Import Namespace="System.Text" %> <%@
Import Namespace="System.Web" %> <%@ Import Namespace="System.Web.Hosting" %>
<%@ Import Namespace="WebApplication" %>

<script runat="server">
  // ============================================================================
  // VPP LAB (existing) — unchanged handlers, moved into View 1 of MultiView
  // ============================================================================
  protected void Page_Load(object sender, EventArgs e)
  {
      UpdateStatus();
      UpdateDeserializationStatus();
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

  // ============================================================================
  // Tab switching (LinkButtons re-target MultiView.ActiveViewIndex)
  // ============================================================================
  protected void lnkVpp_Click(object sender, EventArgs e)    { mv.ActiveViewIndex = 0; }
  protected void lnkDeser_Click(object sender, EventArgs e)  { mv.ActiveViewIndex = 1; }

  // ============================================================================
  // DESERIALIZATION LAB (new) — 4 sinks + child-process detector
  // ============================================================================
  protected void btnLoadBenign_Click(object sender, EventArgs e)
  {
      var sink = rblSink.SelectedValue;
      txtPayload.Text = DeserializationLab.BenignFor(sink);
      mv.ActiveViewIndex = 1;
      UpdateDeserializationStatus();
  }

  protected void btnDeserialize_Click(object sender, EventArgs e)
  {
      var sink = rblSink.SelectedValue;
      var payload = txtPayload.Text;
      DeserializationLab.Deserialize(sink, payload);
      mv.ActiveViewIndex = 1;
      UpdateDeserializationStatus();
  }

  protected void btnRefreshChildren_Click(object sender, EventArgs e)
  {
      mv.ActiveViewIndex = 1;
      UpdateDeserializationStatus();
  }

  private void UpdateDeserializationStatus()
  {
      // My PID (this w3wp.exe)
      var myPid = Process.GetCurrentProcess().Id;
      var procName = Process.GetCurrentProcess().ProcessName;
      lblProcess.Text = HttpUtility.HtmlEncode(procName) + " (PID " + myPid + ")";

      // Recent attempts
      var attempts = DeserializationLab.Recent();
      if (attempts.Count == 0)
      {
          lblAttempts.Text = "<em>no attempts yet</em>";
      }
      else
      {
          var sb = new StringBuilder();
          sb.Append("<table border='1' cellpadding='4' cellspacing='0' style='border-collapse:collapse;font-family:monospace;font-size:12px'>");
          sb.Append("<tr style='background:#e0e0e0'><th>UTC</th><th>Sink</th><th>Bytes</th><th>Result</th><th>Message</th></tr>");
          foreach (var a in attempts)
          {
              sb.Append("<tr>");
              sb.Append("<td>").Append(a.Utc.ToString("HH:mm:ss.fff")).Append("</td>");
              sb.Append("<td>").Append(HttpUtility.HtmlEncode(a.Sink)).Append("</td>");
              sb.Append("<td>").Append(a.PayloadBytes).Append("</td>");
              sb.Append("<td style='color:").Append(a.Ok ? "green" : "orange").Append("'>")
                .Append(a.Ok ? "ok" : "exception").Append("</td>");
              sb.Append("<td>").Append(HttpUtility.HtmlEncode(a.Message)).Append("</td>");
              sb.Append("</tr>");
          }
          sb.Append("</table>");
          lblAttempts.Text = sb.ToString();
      }

      // Child processes
      var children = DeserializationLab.ListChildren();
      if (children.Count == 0)
      {
          lblChildren.Text = "<em>no child processes — nothing spawned by " +
                             HttpUtility.HtmlEncode(procName) + " (PID " + myPid + ")</em>";
      }
      else
      {
          var sb = new StringBuilder();
          var anySuspicious = false;
          sb.Append("<table border='1' cellpadding='4' cellspacing='0' style='border-collapse:collapse;font-family:monospace;font-size:12px'>");
          sb.Append("<tr style='background:#e0e0e0'><th>PID</th><th>Name</th><th>Verdict</th><th>Command line</th></tr>");
          foreach (var c in children)
          {
              if (c.Suspicious) anySuspicious = true;
              sb.Append("<tr");
              if (c.Suspicious) sb.Append(" style='background:#ffe0e0'");
              sb.Append("><td>").Append(c.Pid).Append("</td>");
              sb.Append("<td>").Append(HttpUtility.HtmlEncode(c.Name ?? "")).Append("</td>");
              sb.Append("<td>");
              if (c.Suspicious) sb.Append("<b style='color:#c00'>EXPLOIT</b>");
              else              sb.Append("<span style='color:#666'>ok</span>");
              sb.Append("</td>");
              sb.Append("<td>").Append(HttpUtility.HtmlEncode(c.CommandLine ?? "")).Append("</td>");
              sb.Append("</tr>");
          }
          sb.Append("</table>");
          if (anySuspicious)
          {
              sb.Insert(0, "<div style='padding:8px;background:#ffe0e0;border:1px solid #c00;font-weight:bold;margin-bottom:8px'>"
                          + "&#9888; SUSPICIOUS CHILD PROCESS DETECTED &mdash; deserialization exploit landed.</div>");
          }
          lblChildren.Text = sb.ToString();
      }
  }
</script>

<!DOCTYPE html>
<html>
  <head runat="server">
    <title>CVE Lab</title>
    <style>
      body {
        font-family:
          Segoe UI,
          sans-serif;
        margin: 20px;
      }
      .tab {
        display: inline-block;
        padding: 6px 14px;
        margin-right: 4px;
        border: 1px solid #888;
        background: #f0f0f0;
        text-decoration: none;
        color: #333;
        font-weight: 600;
      }
      .tab-active {
        background: #fff;
        border-bottom: 1px solid #fff;
        color: #000;
      }
      .tab-panel {
        border: 1px solid #888;
        padding: 16px;
        margin-top: -1px;
      }
      textarea.payload {
        width: 100%;
        height: 180px;
        font-family: Consolas, monospace;
        font-size: 12px;
      }
      .warn {
        background: #fff5e6;
        border: 1px solid #d90;
        padding: 6px 10px;
        margin-bottom: 12px;
      }
    </style>
  </head>
  <body>
    <form id="form1" runat="server">
      <h2>CVEonDeserializationFinder &mdash; test lab</h2>

      <div class="warn">
        <b>Warning:</b> this page hosts deliberately vulnerable deserialization
        sinks. Do not deploy anywhere reachable from an untrusted network. Local
        IIS binding <code>http://localhost:8088</code> only.
      </div>

      <asp:LinkButton
        ID="lnkVpp"
        runat="server"
        CssClass="tab"
        OnClick="lnkVpp_Click"
        >1. VPP Lab</asp:LinkButton
      >
      <asp:LinkButton
        ID="lnkDeser"
        runat="server"
        CssClass="tab"
        OnClick="lnkDeser_Click"
        >2. Deserialization Lab</asp:LinkButton
      >

      <div class="tab-panel">
        <asp:MultiView ID="mv" runat="server" ActiveViewIndex="0">
          <!-- =========================================================== -->
          <!-- View 1: VPP Lab                                             -->
          <!-- =========================================================== -->
          <asp:View ID="vwVpp" runat="server">
            <h3>VirtualPathProvider Lab</h3>
            <asp:Button
              ID="btnRegister"
              runat="server"
              Text="Register VPP (and activate)"
              OnClick="btnRegister_Click"
            />
            <asp:Button
              ID="btnDeactivate"
              runat="server"
              Text="Deactivate (AND unregister)"
              OnClick="btnDeactivate_Click"
            />
            <asp:Button
              ID="btnFlushCache"
              runat="server"
              Text="Flush disk compilation cache"
              OnClick="btnFlushCache_Click"
            />
            <hr />
            <asp:Label ID="lblStatus" runat="server" />
            <br />
            <asp:Label ID="lblCacheResult" runat="server" ForeColor="Green" />
          </asp:View>

          <!-- =========================================================== -->
          <!-- View 2: Deserialization Lab                                 -->
          <!-- =========================================================== -->
          <asp:View ID="vwDeser" runat="server">
            <h3>Deserialization Lab</h3>
            <p>
              This host process:
              <b><asp:Label ID="lblProcess" runat="server" /></b>. Pick a sink,
              paste a base64-encoded payload, then <b>Deserialize</b>. A
              ysoserial payload with <code>-c 'ping ya.ru -n 10'</code> should
              spawn a child <code>ping.exe</code> which the
              <em>Child processes</em> table below will flag red.
            </p>

            <fieldset>
              <legend>1. Choose deserialization sink</legend>
              <asp:RadioButtonList
                ID="rblSink"
                runat="server"
                RepeatLayout="Flow"
                RepeatDirection="Vertical"
              >
                <asp:ListItem
                  Value="bf"
                  Text="BinaryFormatter (System.Runtime.Serialization.Formatters.Binary)"
                  Selected="True"
                />
                <asp:ListItem
                  Value="xml"
                  Text="XmlSerializer &mdash; ToolShell / CVE-2025-53770 (List&lt;ExpandedWrapper&lt;LosFormatter, ObjectDataProvider&gt;&gt;)"
                />
                <asp:ListItem
                  Value="los"
                  Text="LosFormatter (System.Web.UI.LosFormatter)"
                />
                <asp:ListItem
                  Value="osf"
                  Text="ObjectStateFormatter (System.Web.UI, default ASP.NET ViewState)"
                />
              </asp:RadioButtonList>
            </fieldset>

            <fieldset>
              <legend>2. Base64 payload</legend>
              <asp:TextBox
                ID="txtPayload"
                runat="server"
                TextMode="MultiLine"
                CssClass="payload"
              />
              <br />
              <asp:Button
                ID="btnLoadBenign"
                runat="server"
                Text="Load benign example for selected sink"
                OnClick="btnLoadBenign_Click"
              />
              <asp:Button
                ID="btnDeserialize"
                runat="server"
                Text="Deserialize"
                OnClick="btnDeserialize_Click"
              />
            </fieldset>

            <fieldset>
              <legend>3. Recent deserialization attempts</legend>
              <asp:Label ID="lblAttempts" runat="server" />
            </fieldset>

            <fieldset>
              <legend>
                4. Child processes of
                <asp:Literal ID="litHostName" runat="server" Text="this w3wp" />
              </legend>
              <asp:Button
                ID="btnRefreshChildren"
                runat="server"
                Text="Refresh child-process list"
                OnClick="btnRefreshChildren_Click"
              />
              <br /><br />
              <asp:Label ID="lblChildren" runat="server" />
            </fieldset>
          </asp:View>
        </asp:MultiView>
      </div>
    </form>
  </body>
</html>
