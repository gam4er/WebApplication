<%@ WebHandler Language="C#" Class="WebApplication.ApiDeserializeHandler" %>

using System;
using System.Web;

namespace WebApplication
{
    /// <summary>
    /// Minimal HTTP endpoint for automated deserialization lab triggering.
    /// POST form fields:
    /// - sink: bf|xml|los|osf
    /// - payload: base64 serialized payload
    /// </summary>
    public sealed class ApiDeserializeHandler : IHttpHandler
    {
        public bool IsReusable => false;

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json";

            var sink = (context.Request["sink"] ?? string.Empty).Trim();
            var payload = context.Request["payload"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(sink) || string.IsNullOrWhiteSpace(payload))
            {
                context.Response.StatusCode = 400;
                context.Response.Write("{\"ok\":false,\"error\":\"sink and payload are required\"}");
                return;
            }

            var attempt = DeserializationLab.Deserialize(sink, payload);
            var msg = JsonEscape(attempt?.Message ?? string.Empty);
            var sinkEscaped = JsonEscape(attempt?.Sink ?? sink);
            var ok = attempt != null && attempt.Ok;
            var bytes = attempt?.PayloadBytes ?? 0;

            context.Response.Write(
                "{\"ok\":" + (ok ? "true" : "false") +
                ",\"sink\":\"" + sinkEscaped + "\"" +
                ",\"bytes\":" + bytes +
                ",\"message\":\"" + msg + "\"}");
        }

        private static string JsonEscape(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
