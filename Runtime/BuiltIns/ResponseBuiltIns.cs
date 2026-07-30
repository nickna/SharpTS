using System.Text;
using System.Text.Json;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides static methods for the Response namespace (Response.json, Response.redirect, Response.error).
/// </summary>
public static class ResponseBuiltIns
{
    /// <summary>
    /// Gets a static method from the Response namespace.
    /// </summary>
    public static object? GetStaticMethod(string name)
    {
        return name switch
        {
            "json" => BuiltInMethod.CreateV2("json", 1, 2, static (_, _, args) =>
            {
                var data = args.Length > 0 ? args[0].ToObject() : null;
                var init = args.Length > 1 ? args[1].ToObject() as SharpTSObject : null;

                var json = SerializeToJson(data);
                var bodyBytes = Encoding.UTF8.GetBytes(json);

                double status = 200;
                string statusText = "";
                var headers = new SharpTSHeaders();
                headers.Set("content-type", "application/json");

                if (init != null)
                {
                    if (init.Fields.TryGetValue("status", out var statusObj) && statusObj is double s)
                        status = s;
                    if (init.Fields.TryGetValue("statusText", out var stObj) && stObj is string st)
                        statusText = st;
                    if (init.Fields.TryGetValue("headers", out var headersObj))
                    {
                        var extraHeaders = headersObj switch
                        {
                            SharpTSHeaders h => h,
                            SharpTSObject obj => new SharpTSHeaders(obj),
                            _ => null
                        };
                        if (extraHeaders != null)
                        {
                            foreach (var entry in extraHeaders.GetEntries())
                                headers.Set(entry.Key, entry.Value);
                        }
                    }
                }

                return RuntimeValue.FromObject(new SharpTSResponse(status, statusText, headers, bodyBytes, "default"));
            }),

            "redirect" => BuiltInMethod.CreateV2("redirect", 1, 2, static (_, _, args) =>
            {
                var url = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "" : "";
                var status = args.Length > 1 && args[1].IsNumber ? args[1].AsNumberUnsafe() : 302.0;

                var headers = new SharpTSHeaders();
                headers.Set("location", url);

                return RuntimeValue.FromObject(new SharpTSResponse(status, "", headers, [], "default"));
            }),

            "error" => BuiltInMethod.CreateV2("error", 0, static (_, _, _) =>
            {
                return RuntimeValue.FromObject(new SharpTSResponse(0, "", new SharpTSHeaders(), [], "error"));
            }),

            _ => null
        };
    }

    private static string SerializeToJson(object? value)
    {
        return JsonGraphWriter.Write(ConvertToSerializable(value));
    }

    private static object? ConvertToSerializable(object? value)
    {
        return value switch
        {
            null => null,
            bool b => b,
            double d => d,
            string s => s,
            SharpTSArray arr => arr.Select(ConvertToSerializable).ToArray(),
            SharpTSObject obj => obj.Fields.ToDictionary(kv => kv.Key, kv => ConvertToSerializable(kv.Value)),
            SharpTSUndefined => null,
            _ => value.ToString()
        };
    }
}
