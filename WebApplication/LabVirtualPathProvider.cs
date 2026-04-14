using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Hosting;

namespace WebApplication
{
    public static class LabVppState
    {
        public static volatile bool Registered = false;
        public static volatile bool Active = false;

        public static readonly string Token = "labtoken-change-me";
    }

    public sealed class LabVirtualPathProvider : VirtualPathProvider
    {
        private static readonly string TargetPath = "~/googlecheck.aspx";

        private static bool IsAuthorizedRequest()
        {
            var ctx = HttpContext.Current;
            if (ctx == null) return false;

            var token = ctx.Request.QueryString["token"];
            if (string.IsNullOrEmpty(token)) return false;

            return string.Equals(token, LabVppState.Token, StringComparison.Ordinal);
        }

        private VirtualPathProvider PrevOrNull => Previous;

        public override bool FileExists(string virtualPath)
        {
            if (LabVppState.Active && IsAuthorizedRequest())
            {
                var appRel = VirtualPathUtility.ToAppRelative(virtualPath);
                if (string.Equals(appRel, TargetPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (PrevOrNull != null) return PrevOrNull.FileExists(virtualPath);
            return base.FileExists(virtualPath);
        }

        public override VirtualFile GetFile(string virtualPath)
        {
            if (LabVppState.Active && IsAuthorizedRequest())
            {
                var appRel = VirtualPathUtility.ToAppRelative(virtualPath);
                if (string.Equals(appRel, TargetPath, StringComparison.OrdinalIgnoreCase))
                    return new LabVirtualFile(virtualPath);
            }

            if (PrevOrNull != null) return PrevOrNull.GetFile(virtualPath);
            return base.GetFile(virtualPath);
        }
    }

    public sealed class LabVirtualFile : VirtualFile
    {
        public LabVirtualFile(string virtualPath) : base(virtualPath) { }

        public override Stream Open()
        {
            var aspx = @"
<%@ Page Language=""C#"" %>
<%@ Import Namespace=""System"" %>
<%@ Import Namespace=""System.Net"" %>

<script runat=""server"">
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = ""text/plain"";
        Response.Write(CheckGoogle());
    }

    private static string CheckGoogle()
    {
        try
        {
            var req = (HttpWebRequest)WebRequest.Create(""https://www.google.com/generate_204"");
            req.Method = ""GET"";
            req.Timeout = 2000;
            req.ReadWriteTimeout = 2000;

            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                return ""google.com reachable; HTTP "" + ((int)resp.StatusCode).ToString();
            }
        }
        catch (Exception ex)
        {
            return ""google.com unreachable; "" + ex.GetType().FullName + "": "" + ex.Message;
        }
    }
</script>
";
            return new MemoryStream(Encoding.UTF8.GetBytes(aspx));
        }
    }
}
