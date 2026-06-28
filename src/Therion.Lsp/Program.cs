// EXT-05 — therion-lsp: a minimal Language Server exposing TherionProc's parser/semantics to any
// LSP client (VSCode/Neovim/…). Speaks JSON-RPC over stdio (Content-Length framed) and publishes
// diagnostics on open/change. Hover/definition can be layered on later; diagnostics are the core.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Therion.Lsp;

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();
var docs = new Dictionary<string, string>(StringComparer.Ordinal);   // uri → text
var jsonOpts = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

while (true)
{
    var body = ReadMessage(stdin);
    if (body is null) break;   // stdin closed

    JsonDocument doc;
    try { doc = JsonDocument.Parse(body); }
    catch { continue; }

    using (doc)
    {
        var root = doc.RootElement;
        var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
        JsonElement id = root.TryGetProperty("id", out var idEl) ? idEl.Clone() : default;
        bool hasId = root.TryGetProperty("id", out _);

        switch (method)
        {
            case "initialize":
                Respond(id, new
                {
                    capabilities = new { textDocumentSync = 1 },   // 1 = full document sync
                    serverInfo = new { name = "therion-lsp", version = "0.1" },
                });
                break;

            case "shutdown":
                Respond(id, (object?)null);
                break;

            case "exit":
                return 0;

            case "textDocument/didOpen":
                if (TryDoc(root, out var ou, out var ot)) { docs[ou] = ot; Publish(ou, ot); }
                break;

            case "textDocument/didChange":
                if (TryChange(root, out var cu, out var ctext)) { docs[cu] = ctext; Publish(cu, ctext); }
                break;

            case "textDocument/didClose":
                if (TryUri(root, out var clu)) { docs.Remove(clu); PublishEmpty(clu); }
                break;

            default:
                // Unknown request → empty result so clients don't hang; notifications are ignored.
                if (hasId) Respond(id, (object?)null);
                break;
        }
    }
}
return 0;

// ---- handlers / helpers ----

void Respond(JsonElement id, object? result) =>
    WriteMessage(new { jsonrpc = "2.0", id, result });

void Publish(string uri, string text)
{
    var diags = DiagnosticProvider.Compute(UriToPath(uri), text).Select(d => new
    {
        range = new
        {
            start = new { line = d.Range.Start.Line, character = d.Range.Start.Character },
            end = new { line = d.Range.End.Line, character = d.Range.End.Character },
        },
        severity = d.Severity,
        code = d.Code,
        source = d.Source,
        message = d.Message,
    }).ToArray();
    WriteMessage(new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = new { uri, diagnostics = diags } });
}

void PublishEmpty(string uri) =>
    WriteMessage(new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = new { uri, diagnostics = Array.Empty<object>() } });

void WriteMessage(object payload)
{
    var json = JsonSerializer.SerializeToUtf8Bytes(payload, jsonOpts);
    var header = Encoding.ASCII.GetBytes($"Content-Length: {json.Length}\r\n\r\n");
    stdout.Write(header, 0, header.Length);
    stdout.Write(json, 0, json.Length);
    stdout.Flush();
}

static bool TryDoc(JsonElement root, out string uri, out string text)
{
    uri = ""; text = "";
    if (!root.TryGetProperty("params", out var p) || !p.TryGetProperty("textDocument", out var td)) return false;
    if (!td.TryGetProperty("uri", out var u)) return false;
    uri = u.GetString() ?? "";
    text = td.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
    return uri.Length > 0;
}

static bool TryChange(JsonElement root, out string uri, out string text)
{
    uri = ""; text = "";
    if (!root.TryGetProperty("params", out var p)) return false;
    if (!p.TryGetProperty("textDocument", out var td) || !td.TryGetProperty("uri", out var u)) return false;
    uri = u.GetString() ?? "";
    // Full sync: take the last content change's full text.
    if (!p.TryGetProperty("contentChanges", out var changes) || changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() == 0)
        return false;
    var last = changes[changes.GetArrayLength() - 1];
    text = last.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
    return uri.Length > 0;
}

static bool TryUri(JsonElement root, out string uri)
{
    uri = "";
    if (!root.TryGetProperty("params", out var p) || !p.TryGetProperty("textDocument", out var td)) return false;
    if (!td.TryGetProperty("uri", out var u)) return false;
    uri = u.GetString() ?? "";
    return uri.Length > 0;
}

static string UriToPath(string uri)
{
    try { return uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(uri).LocalPath : uri; }
    catch { return uri; }
}

static string? ReadMessage(Stream input)
{
    int contentLength = -1;
    while (true)
    {
        var line = ReadLine(input);
        if (line is null) return null;          // EOF
        if (line.Length == 0) break;            // blank line ends the headers
        int idx = line.IndexOf(':');
        if (idx > 0 && line[..idx].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            int.TryParse(line[(idx + 1)..].Trim(), out contentLength);
    }
    if (contentLength < 0) return null;

    var buf = new byte[contentLength];
    int read = 0;
    while (read < contentLength)
    {
        int n = input.Read(buf, read, contentLength - read);
        if (n <= 0) return null;
        read += n;
    }
    return Encoding.UTF8.GetString(buf);
}

static string? ReadLine(Stream s)
{
    var bytes = new List<byte>();
    int b;
    while ((b = s.ReadByte()) != -1)
    {
        if (b == '\n')
        {
            if (bytes.Count > 0 && bytes[^1] == (byte)'\r') bytes.RemoveAt(bytes.Count - 1);
            return Encoding.ASCII.GetString(bytes.ToArray());
        }
        bytes.Add((byte)b);
    }
    return bytes.Count > 0 ? Encoding.ASCII.GetString(bytes.ToArray()) : null;
}
