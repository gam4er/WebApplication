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
        /// Deactivates the VPP, wipes the disk compilation cache
        /// (Temporary ASP.NET Files) for this application, and then
        /// restarts the AppDomain. This is the only reliable way to
        /// fully remove a registered VPP on full IIS, where the
        /// DiskBuildResultCache survives AppDomain recycles.
        /// </summary>
        public static void DeactivateAndUnload()
        {
            Active = false;
            Registered = false;
            FlushDiskCompilationCache();
            HttpRuntime.UnloadAppDomain();
        }

        /// <summary>
        /// Deletes all files in the Temporary ASP.NET Files folder for
        /// the current application. On full IIS this is the
        /// DiskBuildResultCache that persists compiled virtual pages
        /// across AppDomain restarts and even app pool recycles.
        /// Returns a human-readable status string.
        /// </summary>
        public static string FlushDiskCompilationCache()
        {
            try
            {
                var tempDir = HttpRuntime.CodegenDir;
                if (string.IsNullOrEmpty(tempDir) || !Directory.Exists(tempDir))
                    return "CodegenDir not found: " + (tempDir ?? "(null)");

                int deleted = 0;
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); deleted++; }
                    catch { /* locked by current AppDomain — will be gone after recycle */ }
                }
                foreach (var dir in Directory.GetDirectories(tempDir))
                {
                    try { Directory.Delete(dir, true); }
                    catch { }
                }

                return "Deleted " + deleted + " file(s) from " + tempDir;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
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
