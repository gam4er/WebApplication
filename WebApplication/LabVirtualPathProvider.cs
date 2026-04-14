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

        /// <summary>
        /// Deactivates the VPP and restarts the AppDomain to flush the
        /// ASP.NET compilation cache. HostingEnvironment has no public
        /// UnregisterVirtualPathProvider, so an AppDomain recycle is the
        /// only reliable way to fully remove a registered provider.
        /// </summary>
        public static void DeactivateAndUnload()
        {
            Active = false;
            Registered = false;
            HttpRuntime.UnloadAppDomain();
        }
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

        private bool IsTargetPath(string virtualPath)
        {
            var appRel = VirtualPathUtility.ToAppRelative(virtualPath);
            return string.Equals(appRel, TargetPath, StringComparison.OrdinalIgnoreCase);
        }

        private VirtualPathProvider PrevOrNull => Previous;

        public override bool FileExists(string virtualPath)
        {
            if (LabVppState.Active && IsAuthorizedRequest() && IsTargetPath(virtualPath))
                return true;

            if (PrevOrNull != null) return PrevOrNull.FileExists(virtualPath);
            return base.FileExists(virtualPath);
        }

        public override VirtualFile GetFile(string virtualPath)
        {
            if (LabVppState.Active && IsAuthorizedRequest() && IsTargetPath(virtualPath))
                return new LabVirtualFile(virtualPath);

            if (PrevOrNull != null) return PrevOrNull.GetFile(virtualPath);
            return base.GetFile(virtualPath);
        }

        /// <summary>
        /// Returns null for the virtual path served by this provider so that
        /// ASP.NET does not attempt to set up FileChangesMonitor on a
        /// non-existent physical directory.
        /// </summary>
        public override CacheDependency GetCacheDependency(
            string virtualPath, IEnumerable virtualPathDependencies, DateTime utcStart)
        {
            if (IsTargetPath(virtualPath))
                return null;

            if (PrevOrNull != null)
                return PrevOrNull.GetCacheDependency(virtualPath, virtualPathDependencies, utcStart);
            return base.GetCacheDependency(virtualPath, virtualPathDependencies, utcStart);
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
