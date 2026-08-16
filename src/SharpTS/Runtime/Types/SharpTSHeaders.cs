using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the Web API Headers class.
/// Provides case-insensitive header storage with multi-value support.
/// </summary>
public class SharpTSHeaders : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly Dictionary<string, List<string>> _headers;

    /// <summary>
    /// Creates an empty Headers object.
    /// </summary>
    public SharpTSHeaders()
    {
        _headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a Headers object from a dictionary (e.g., from response headers).
    /// Values may be strings, string[], <see cref="List{T}"/> of string, or other
    /// objects (ToString'd). Array/list values are stored as multi-value entries —
    /// this is how Set-Cookie preserves its individual cookies through the
    /// compiled-mode fetch path.
    /// </summary>
    public SharpTSHeaders(Dictionary<string, object?> init)
        : this()
    {
        foreach (var kv in init)
        {
            switch (kv.Value)
            {
                case string s:
                    _headers[kv.Key] = [s];
                    break;
                case string[] arr:
                    _headers[kv.Key] = [.. arr];
                    break;
                case List<string> list:
                    _headers[kv.Key] = [.. list];
                    break;
                default:
                    _headers[kv.Key] = [kv.Value?.ToString() ?? ""];
                    break;
            }
        }
    }

    /// <summary>
    /// Creates a Headers object from an HttpResponseMessage's headers.
    /// </summary>
    /// <remarks>
    /// Set-Cookie is preserved as a list of distinct values (each header line is a
    /// separate cookie). All other multi-valued headers are joined with ", " — that
    /// matches the WHATWG fetch combined-value semantics.
    /// </remarks>
    public SharpTSHeaders(System.Net.Http.HttpResponseMessage response)
        : this()
    {
        foreach (var header in response.Headers)
        {
            if (string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                _headers[header.Key] = [.. header.Value];
            }
            else
            {
                _headers[header.Key] = [string.Join(", ", header.Value)];
            }
        }

        if (response.Content?.Headers != null)
        {
            foreach (var header in response.Content.Headers)
            {
                _headers[header.Key] = [string.Join(", ", header.Value)];
            }
        }
    }

    /// <summary>
    /// Creates a Headers from a SharpTSObject (for interpreter: new Headers({...})).
    /// </summary>
    public SharpTSHeaders(SharpTSObject obj)
        : this()
    {
        foreach (var kv in obj.Fields)
        {
            var value = kv.Value?.ToString() ?? "";
            _headers[kv.Key] = [value];
        }
    }

    /// <summary>
    /// Gets the value of a header. Multiple values are joined with ", ", except
    /// for <c>Set-Cookie</c> which per the WHATWG fetch spec returns the first
    /// value only (use <see cref="GetSetCookie"/> for the full list).
    /// Returns null if the header doesn't exist.
    /// </summary>
    public string? Get(string name)
    {
        if (_headers.TryGetValue(name, out var values))
        {
            if (string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                return values.Count > 0 ? values[0] : null;
            }
            return string.Join(", ", values);
        }
        return null;
    }

    /// <summary>
    /// Returns all <c>Set-Cookie</c> header values as a list. Empty list if there
    /// are no Set-Cookie headers. This mirrors the WHATWG fetch
    /// <c>headers.getSetCookie()</c> method.
    /// </summary>
    public IList<string> GetSetCookie()
    {
        if (_headers.TryGetValue("Set-Cookie", out var values))
        {
            return values.ToList();
        }
        return [];
    }

    /// <summary>
    /// Sets a header, replacing any existing values.
    /// </summary>
    public void Set(string name, string value)
    {
        _headers[name] = [value];
    }

    /// <summary>
    /// Returns whether a header with the given name exists.
    /// </summary>
    public bool Has(string name)
    {
        return _headers.ContainsKey(name);
    }

    /// <summary>
    /// Deletes a header by name.
    /// </summary>
    public bool Delete(string name)
    {
        return _headers.Remove(name);
    }

    /// <summary>
    /// Appends a value to a header without replacing existing values.
    /// </summary>
    public void Append(string name, string value)
    {
        if (_headers.TryGetValue(name, out var values))
        {
            values.Add(value);
        }
        else
        {
            _headers[name] = [value];
        }
    }

    /// <summary>
    /// Returns all header entries as key-value pairs (lowercase keys, joined values).
    /// Per the WHATWG fetch spec, <c>Set-Cookie</c> yields one entry per cookie
    /// rather than a single comma-joined entry.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> GetEntries()
    {
        foreach (var kv in _headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
        {
            var lowerKey = kv.Key.ToLowerInvariant();
            if (lowerKey == "set-cookie")
            {
                foreach (var v in kv.Value)
                {
                    yield return new KeyValuePair<string, string>(lowerKey, v);
                }
            }
            else
            {
                yield return new KeyValuePair<string, string>(
                    lowerKey,
                    string.Join(", ", kv.Value));
            }
        }
    }

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "get" => BuiltInMethod.CreateV2("get", 1, (_, _, args) =>
            {
                var headerName = args[0].ToObject()?.ToString() ?? "";
                return RuntimeValue.FromBoxed(Get(headerName));
            }),
            "set" => BuiltInMethod.CreateV2("set", 2, (_, _, args) =>
            {
                var headerName = args[0].ToObject()?.ToString() ?? "";
                var headerValue = args.Length > 1 ? args[1].ToObject()?.ToString() ?? "" : "";
                Set(headerName, headerValue);
                return RuntimeValue.Undefined;
            }),
            "has" => BuiltInMethod.CreateV2("has", 1, (_, _, args) =>
            {
                var headerName = args[0].ToObject()?.ToString() ?? "";
                return RuntimeValue.FromBoolean(Has(headerName));
            }),
            "delete" => BuiltInMethod.CreateV2("delete", 1, (_, _, args) =>
            {
                var headerName = args[0].ToObject()?.ToString() ?? "";
                return RuntimeValue.FromBoolean(Delete(headerName));
            }),
            "append" => BuiltInMethod.CreateV2("append", 2, (_, _, args) =>
            {
                var headerName = args[0].ToObject()?.ToString() ?? "";
                var headerValue = args.Length > 1 ? args[1].ToObject()?.ToString() ?? "" : "";
                Append(headerName, headerValue);
                return RuntimeValue.Undefined;
            }),
            "getSetCookie" => BuiltInMethod.CreateV2("getSetCookie", 0, (_, _, _) =>
            {
                var cookies = GetSetCookie();
                return RuntimeValue.FromObject(new SharpTSArray(cookies.Select(c => (object?)c).ToList()));
            }),
            "forEach" => BuiltInMethod.CreateV2("forEach", 1, (interp, _, args) =>
            {
                var callback = args[0].ToObject();
                foreach (var entry in GetEntries())
                {
                    CallCallback(interp, callback, entry.Value, entry.Key);
                }
                return RuntimeValue.Undefined;
            }),
            "entries" => BuiltInMethod.CreateV2("entries", 0, (_, _, _) =>
            {
                var entries = GetEntries()
                    .Select(e => (object?)new SharpTSArray([e.Key, e.Value]))
                    .ToList();
                return RuntimeValue.FromObject(new SharpTSIterator(entries));
            }),
            "keys" => BuiltInMethod.CreateV2("keys", 0, (_, _, _) =>
            {
                var keys = GetEntries()
                    .Select(e => (object?)e.Key)
                    .ToList();
                return RuntimeValue.FromObject(new SharpTSIterator(keys));
            }),
            "values" => BuiltInMethod.CreateV2("values", 0, (_, _, _) =>
            {
                var values = GetEntries()
                    .Select(e => (object?)e.Value)
                    .ToList();
                return RuntimeValue.FromObject(new SharpTSIterator(values));
            }),
            _ => null
        };
    }

    /// <summary>
    /// Calls a callback function (supports ISharpTSCallable, BuiltInMethod, and TSFunction).
    /// </summary>
    private static void CallCallback(Execution.Interpreter? interp, object? callback, string value, string key)
    {
        if (callback is ISharpTSCallable callable)
        {
            callable.Call(interp!, [value, key]);
        }
    }

    public override string ToString() => "Headers {}";
}
