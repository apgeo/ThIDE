// The UI-automation tool catalog: lets a test or a coding agent see and drive the running app like a
// user would, through the automation-peer tree. Served only by UiAutomationHost (opt-in by environment
// variable); never part of the user-facing project MCP server.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Threading;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ThIDE.Services.UiAutomation;

[McpServerToolType]
public sealed class UiAutomationTools
{
    private const string SelectorHelp =
        "Selector: '#AutomationId', 'name:Exact name', 'name~:regex', 'role:Button' (automation control type), "
        + "'type:TextBox' (control class), 'text~:regex', 'state:selected|focused|checked|expanded|enabled|showing', "
        + "or a bare string (= exact name). Join terms with ';', chain descendants with ' >> ', pick the n-th "
        + "match with a trailing '@n'. Example: 'role:TabItem;name:3D Viewer'. Tools that act on an element "
        + "wait up to 5 s for it to appear.";

    /// <summary>How long element-targeting tools keep looking for a selector that matches nothing yet.</summary>
    private const int AutoWaitMs = 5000;

    // Runs f on the UI thread. A "no element matches" failure is retried for AutoWaitMs, so a call
    // right after a click doesn't race the UI (f looks its element up before it changes anything).
    private static async Task<T> OnUi<T>(Func<T> f)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try { return await Dispatcher.UIThread.InvokeAsync(f); }
            catch (UiElementNotFoundException) when (sw.ElapsedMilliseconds < AutoWaitMs) { await Task.Delay(150); }
            catch (UiAutomationException ex) { throw new McpException(ex.Message); }
            catch (FormatException ex) { throw new McpException("Bad selector: " + ex.Message); }
        }
    }

    [McpServerTool(Name = "ui_windows", Title = "List windows", ReadOnly = true)]
    [Description("Lists the app's open windows (main window, dialogs, floating panes) with title, automation id, "
               + "bounds and which one is active. Start here.")]
    public Task<string> Windows() => OnUi(() =>
    {
        var sb = new StringBuilder();
        foreach (var (w, i) in UiTree.Windows().Select((w, i) => (w, i)))
        {
            var n = UiTree.Snapshot(ControlAutomationPeer.CreatePeerForElement(w), 0);
            sb.Append(i).Append(": ").Append(UiTree.Line(n.Info)).Append(w.IsActive ? " (active)" : "").AppendLine();
        }
        return sb.Length > 0 ? sb.ToString() : "No windows are open.";
    });

    [McpServerTool(Name = "ui_tree", Title = "Element tree", ReadOnly = true)]
    [Description("Prints the automation tree, one element per line: [Role] \"Name\" #AutomationId {states} @x,y wxh "
               + "text=\"…\" <ControlType>. Layout-only elements and hidden subtrees (inactive tabs, closed menus) "
               + "are folded away unless asked for. Without a selector, prints every window.")]
    public Task<string> Tree(
        [Description("Root element to print from. " + SelectorHelp)] string? selector = null,
        [Description("Maximum depth below the root.")] int maxDepth = 60,
        [Description("Include hidden subtrees.")] bool includeHidden = false,
        [Description("Include layout-only elements (panels, borders...).")] bool includeLayout = false) => OnUi(() =>
    {
        var roots = selector is null
            ? UiTree.Windows().Select(w => (AutomationPeer)ControlAutomationPeer.CreatePeerForElement(w))
            : new[] { UiTree.Find(selector).Peer };
        var text = string.Concat(roots.Select(r => UiTree.Tree(r, maxDepth, includeHidden, includeLayout)));
        return text.Length > 0 ? text : "(empty)";
    });

    [McpServerTool(Name = "ui_find", Title = "Find elements", ReadOnly = true)]
    [Description("Lists the elements matching a selector (visible ones first). " + SelectorHelp)]
    public Task<string> Find(string selector, [Description("Maximum matches to list.")] int limit = 20) => OnUi(() =>
    {
        var found = UiTree.FindAll(UiSelector.Parse(selector));
        if (found.Count == 0) return $"No element matches '{selector}'.";
        var lines = found.Take(limit).Select(n => UiTree.Line(n.Info)).ToList();
        if (found.Count > limit) lines.Add($"... {found.Count - limit} more");
        return string.Join('\n', lines);
    });

    [McpServerTool(Name = "ui_get", Title = "Element details", ReadOnly = true)]
    [Description("JSON details of the first match: role, name, id, control type, states, bounds, full text "
               + "(text boxes and the code editor included), and any extra property paths read off the control, "
               + "e.g. 'IsChecked', 'SelectedIndex', 'DataContext.Status'. " + SelectorHelp)]
    public Task<string> Get(
        string selector,
        [Description("Extra dotted property paths to read off the control.")] string[]? properties = null) => OnUi(() =>
    {
        var n = UiTree.Find(selector);
        var d = new Dictionary<string, object?>
        {
            ["role"] = n.Info.Role, ["name"] = n.Info.Name, ["id"] = n.Info.Id, ["type"] = n.Info.Type,
            ["states"] = n.Info.States, ["text"] = n.Info.Text,
            ["bounds"] = n.Info.Bounds is var (x, y, w, h) ? new[] { x, y, w, h } : null,
            ["patterns"] = Patterns(n.Peer),
            ["dataContext"] = n.Control?.DataContext?.GetType().Name,
            ["childCount"] = SafeCount(n.Peer),
        };
        if (properties is { Length: > 0 } && n.Control is { } c)
            d["properties"] = properties.ToDictionary(p => p, p => UiTree.ReadPath(c, p));
        return JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true });
    });

    [McpServerTool(Name = "ui_invoke", Title = "Act on element")]
    [Description("Acts on the first match the way a user would: 'default' presses buttons and menu items, toggles "
               + "check boxes, selects tabs/list items and expands/collapses tree nodes. Or name the action: invoke, "
               + "toggle, select, expand, collapse, focus, contextmenu. " + SelectorHelp)]
    public Task<string> Invoke(string selector, string action = "default") => OnUi(() =>
    {
        var n = UiTree.Find(selector);
        if (!n.Info.States.Contains("enabled")) throw new UiAutomationException($"'{selector}' is disabled.");
        return UiTree.Act(n, action) + ": " + UiTree.Line(n.Info);
    });

    [McpServerTool(Name = "ui_menu", Title = "Run a menu command")]
    [Description("Opens a menu path and runs the last item, e.g. 'File > Open File' or 'Build > Build'. Items are "
               + "matched by name, ignoring a trailing '…'/'...', or by automation id ('#Menu.File').")]
    public async Task<string> Menu(string path, CancellationToken ct = default)
    {
        var parts = path.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new McpException("Empty menu path.");
        for (int i = 0; i < parts.Length; i++)
        {
            bool last = i == parts.Length - 1;
            var part = parts[i];
            await Retry(() =>
            {
                var sel = UiSelector.Parse(part.StartsWith('#') ? part : "role:MenuItem");
                var visible = UiTree.FindAll(sel).Where(n => n.Info.States.Contains("showing"))
                    .OrderBy(n => n.Info.Bounds?.Y ?? int.MaxValue) // the menu bar before any open popup
                    .ToList();
                var item = part.StartsWith('#') ? visible.FirstOrDefault()
                    : visible.FirstOrDefault(n => MenuNameEquals(n.Info.Name, part, prefix: false))
                      ?? visible.FirstOrDefault(n => MenuNameEquals(n.Info.Name, part, prefix: true));
                if (item is null)
                    throw new UiAutomationException(
                        $"Menu item '{part}' is not visible (after '{string.Join(" > ", parts.Take(i))}'). Visible items: "
                        + string.Join(", ", visible.Select(n => $"'{n.Info.Name}'")));
                if (!item.Info.States.Contains("enabled")) throw new UiAutomationException($"Menu item '{part}' is disabled.");
                UiTree.Act(item, last ? "invoke" : "expand");
                return item.Info.Id;
            }, ct).ConfigureAwait(false);
        }
        return $"ran '{path}'";
    }

    private static bool MenuNameEquals(string name, string wanted, bool prefix)
    {
        static string Norm(string s) => Regex.Replace(s.Replace("_", ""), @"(…|\.\.\.)$", "").Trim();
        return prefix
            ? Norm(name).StartsWith(Norm(wanted), StringComparison.OrdinalIgnoreCase)
            : string.Equals(Norm(name), Norm(wanted), StringComparison.OrdinalIgnoreCase);
    }

    [McpServerTool(Name = "ui_set_text", Title = "Set text")]
    [Description("Replaces the text of a text box, combo box text or the code editor. Bypasses typing; use "
               + "ui_type to exercise key handling. " + SelectorHelp)]
    public Task<string> SetText(string selector, string text) => OnUi(() => UiTree.SetText(UiTree.Find(selector), text));

    [McpServerTool(Name = "ui_key", Title = "Press keys")]
    [Description("Sends key gestures (Avalonia syntax: 'Ctrl+S', 'Escape', 'Ctrl+Shift+F', 'F5', 'Enter') to the "
               + "focused element, or to the selector's element after focusing it. Several gestures are sent in order.")]
    public Task<string> Key(
        [Description("Gestures, e.g. ['Ctrl+End'] or ['Down','Down','Enter'].")] string[] keys,
        [Description("Optional element to focus first. " + SelectorHelp)] string? selector = null) => OnUi(() =>
    {
        var target = UiTree.KeyboardTarget(selector is null ? null : UiTree.Find(selector));
        foreach (var k in keys) UiTree.Key(target, k);
        return $"sent {string.Join(' ', keys)} to {target.GetType().Name}";
    });

    [McpServerTool(Name = "ui_type", Title = "Type text")]
    [Description("Types text into the focused element (or the selector's element after focusing it) as text input, "
               + "the way a keyboard would. Use ui_key for Enter/Tab/shortcuts.")]
    public Task<string> Type(string text, [Description("Optional element to focus first. " + SelectorHelp)] string? selector = null) => OnUi(() =>
    {
        var target = UiTree.KeyboardTarget(selector is null ? null : UiTree.Find(selector));
        UiTree.Type(target, text);
        return $"typed {text.Length} chars into {target.GetType().Name}";
    });

    [McpServerTool(Name = "ui_wait", Title = "Wait for element", ReadOnly = true)]
    [Description("Waits until an element matching the selector exists (or, with gone=true, until none does). Put "
               + "conditions in the selector, e.g. 'role:TabItem;name:Log;state:selected' or 'text~:Showing'. " + SelectorHelp)]
    public async Task<string> Wait(string selector, bool gone = false, int timeoutMs = 20_000, CancellationToken ct = default)
    {
        var sel = UiSelector.Parse(selector);
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var found = await OnUi(() => UiTree.FindAll(sel).Select(n => UiTree.Line(n.Info)).FirstOrDefault());
            if (gone ? found is null : found is not null) return gone ? "gone" : found!;
            if (sw.ElapsedMilliseconds >= timeoutMs)
                throw new McpException($"'{selector}' did not {(gone ? "disappear" : "appear")} within {timeoutMs} ms.");
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
    }

    [McpServerTool(Name = "ui_screenshot", Title = "Screenshot", ReadOnly = true)]
    [Description("Renders a window or element to PNG and returns the image. Rendering is done by the app itself, so "
               + "it works without a display server, but native child views (the 3D viewer's web view) come out "
               + "blank — use the AT-SPI driver's screen capture for those.")]
    public async Task<IEnumerable<ContentBlock>> Screenshot(
        [Description("Element to render; default: the active window. " + SelectorHelp)] string? selector = null,
        [Description("Also save the PNG to this path.")] string? savePath = null)
    {
        var png = await OnUi(() =>
        {
            Avalonia.Visual visual = selector is null
                ? UiTree.Windows().FirstOrDefault(w => w.IsActive) ?? UiTree.Windows().FirstOrDefault()
                    ?? throw new UiAutomationException("No window is open.")
                : UiTree.Find(selector).Control ?? throw new UiAutomationException("That element has no control to render.");
            return UiTree.RenderPng(visual);
        });
        var blocks = new List<ContentBlock> { ImageContentBlock.FromBytes(png, "image/png") };
        if (savePath is not null)
        {
            var full = Path.GetFullPath(savePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, png).ConfigureAwait(false);
            blocks.Add(new TextContentBlock { Text = "saved " + full });
        }
        return blocks;
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<T> Retry<T>(Func<T> f, CancellationToken ct, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try { return await Dispatcher.UIThread.InvokeAsync(f); }
            catch (UiAutomationException) when (sw.ElapsedMilliseconds < timeoutMs)
            {
                // Popups open asynchronously; give the submenu a moment to appear.
                await Task.Delay(150, ct).ConfigureAwait(false);
            }
            catch (UiAutomationException ex) { throw new McpException(ex.Message); }
        }
    }

    private static List<string> Patterns(AutomationPeer p)
    {
        var l = new List<string>();
        if (p.GetProvider<IInvokeProvider>() is not null) l.Add("invoke");
        if (p.GetProvider<IToggleProvider>() is not null) l.Add("toggle");
        if (p.GetProvider<ISelectionItemProvider>() is not null) l.Add("select");
        if (p.GetProvider<IExpandCollapseProvider>() is not null) l.Add("expand/collapse");
        if (p.GetProvider<IValueProvider>() is not null) l.Add("value");
        if (p.GetProvider<IRangeValueProvider>() is not null) l.Add("range");
        if (p.GetProvider<IScrollProvider>() is not null) l.Add("scroll");
        return l;
    }

    private static int SafeCount(AutomationPeer p)
    {
        try { return p.GetChildren().Count; } catch { return 0; }
    }
}
