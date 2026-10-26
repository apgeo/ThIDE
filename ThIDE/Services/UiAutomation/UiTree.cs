// Walks the running app's automation-peer tree (the same tree AT-SPI / UIA expose) and acts on it:
// find by selector, invoke/toggle/select/expand through the peer providers, raise keyboard input on
// the focused element, read properties, and render controls to PNG. Everything here must run on the
// UI thread; UiAutomationTools marshals every call.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace ThIDE.Services.UiAutomation;

/// <summary>One live element: its peer, owning control (when it has one) and snapshot.</summary>
internal sealed record UiNode(AutomationPeer Peer, Control? Control, UiElementInfo Info, int Depth);

internal static class UiTree
{
    private const int NodeLimit = 50_000;

    public static IReadOnlyList<Window> Windows() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            ? d.Windows.Where(w => w.IsVisible).ToList()
            : Array.Empty<Window>();

    public static IEnumerable<UiNode> Walk(AutomationPeer root, bool includeHidden = true)
    {
        var stack = new Stack<(AutomationPeer Peer, int Depth)>();
        stack.Push((root, 0));
        int seen = 0;
        while (stack.Count > 0 && seen++ < NodeLimit)
        {
            var (peer, depth) = stack.Pop();
            var node = Snapshot(peer, depth);
            if (!includeHidden && depth > 0 && !node.Info.States.Contains("showing")) continue;
            yield return node;
            IReadOnlyList<AutomationPeer> kids;
            try { kids = peer.GetChildren(); }
            catch { continue; }
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], depth + 1));
        }
    }

    public static IEnumerable<UiNode> WalkAll(bool includeHidden = true) =>
        Windows().SelectMany(w => Walk(ControlAutomationPeer.CreatePeerForElement(w), includeHidden));

    public static List<UiNode> FindAll(UiSelector selector)
    {
        var scopes = Windows().Select(w => (UiNode?)Snapshot(ControlAutomationPeer.CreatePeerForElement(w), 0)).ToList();
        for (int step = 0; step < selector.StepCount; step++)
        {
            var next = new List<UiNode?>();
            foreach (var scope in scopes)
            {
                foreach (var n in Walk(scope!.Peer))
                {
                    // A descendant step never matches its own scope element.
                    if (step > 0 && n.Depth == 0) continue;
                    if (selector.MatchesStep(step, n.Info)) next.Add(n);
                }
            }
            scopes = next;
        }
        var found = scopes.Select(n => n!)
            .OrderBy(n => n.Info.States.Contains("showing") ? 0 : 1)
            .ToList();
        if (selector.Index is int i) return i < found.Count ? new List<UiNode> { found[i] } : new List<UiNode>();
        return found;
    }

    public static UiNode Find(string selector)
    {
        var found = FindAll(UiSelector.Parse(selector));
        return found.Count > 0 ? found[0] : throw new UiElementNotFoundException($"No element matches '{selector}'.");
    }

    public static UiNode Snapshot(AutomationPeer peer, int depth)
    {
        var control = (peer as ControlAutomationPeer)?.Owner;
        var states = new List<string>();
        bool Try(Func<bool> f) { try { return f(); } catch { return false; } }

        if (Try(peer.IsEnabled)) states.Add("enabled");
        if (!Try(peer.IsOffscreen) && (control is null || control.IsEffectivelyVisible)) states.Add("showing");
        if (Try(peer.HasKeyboardFocus)) states.Add("focused");
        if (peer.GetProvider<ISelectionItemProvider>() is { } sel && Try(() => sel.IsSelected)) states.Add("selected");
        if (peer.GetProvider<IToggleProvider>() is { } tog)
        {
            var ts = tog.ToggleState;
            if (ts == ToggleState.On) states.Add("checked");
            else if (ts == ToggleState.Indeterminate) states.Add("indeterminate");
        }
        if (peer.GetProvider<IExpandCollapseProvider>() is { } ec && ec.ExpandCollapseState == ExpandCollapseState.Expanded)
            states.Add("expanded");

        string Safe(Func<string?> f) { try { return f() ?? ""; } catch { return ""; } }
        var info = new UiElementInfo(
            Role: Safe(() => peer.GetAutomationControlType().ToString()),
            Type: control?.GetType().Name ?? Safe(peer.GetClassName),
            Name: Safe(peer.GetName),
            Id: Safe(peer.GetAutomationId),
            Text: TextOf(peer, control),
            States: states,
            Bounds: ScreenBounds(control));
        return new UiNode(peer, control, info, depth);
    }

    private static string? TextOf(AutomationPeer peer, Control? control)
    {
        try
        {
            if (peer.GetProvider<IValueProvider>() is { } v) return v.Value;
            // TextBlock, TextBox and AvaloniaEdit's TextEditor all expose a string Text property.
            var p = control?.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
            return p?.PropertyType == typeof(string) ? p.GetValue(control) as string : null;
        }
        catch { return null; }
    }

    /// <summary>Screen-pixel bounds (what xdotool / the AT-SPI driver see), or null when not laid out.</summary>
    private static (int, int, int, int)? ScreenBounds(Control? c)
    {
        try
        {
            if (c is null || TopLevel.GetTopLevel(c) is null || c.Bounds.Width <= 0 || c.Bounds.Height <= 0) return null;
            var tl = c.PointToScreen(new Point(0, 0));
            var br = c.PointToScreen(new Point(c.Bounds.Width, c.Bounds.Height));
            return (tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
        }
        catch { return null; }
    }

    // ---- formatting ------------------------------------------------------------------------

    public static string Line(UiElementInfo e, bool withType = true)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(e.Role).Append(']');
        if (e.Name.Length > 0) sb.Append(' ').Append(Quote(e.Name));
        if (e.Id.Length > 0) sb.Append(" #").Append(e.Id);
        var st = e.States.Where(s => s is not ("enabled" or "showing")).ToList();
        if (!e.States.Contains("enabled")) st.Add("disabled");
        if (!e.States.Contains("showing")) st.Add("hidden");
        if (st.Count > 0) sb.Append(" {").Append(string.Join(',', st)).Append('}');
        if (e.Bounds is var (x, y, w, h)) sb.Append(CultureInfo.InvariantCulture, $" @{x},{y} {w}x{h}");
        if (!string.IsNullOrEmpty(e.Text) && e.Text != e.Name)
            sb.Append(" text=").Append(Quote(e.Text.Length > 80 ? e.Text[..80] + "…" : e.Text));
        if (withType) sb.Append(" <").Append(e.Type).Append('>');
        return sb.ToString();
    }

    private static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";

    /// <summary>Layout-only elements (no id worth targeting, no name, nothing to act on).</summary>
    public static bool IsNoise(UiNode n)
    {
        var e = n.Info;
        if (e.Id.Length > 0 && !e.Id.StartsWith("PART_", StringComparison.Ordinal)) return false;
        if (e.Name.Length > 0) return false;
        if (!string.IsNullOrEmpty(e.Text)) return false;
        return n.Peer.GetProvider<IInvokeProvider>() is null
            && n.Peer.GetProvider<IToggleProvider>() is null
            && n.Peer.GetProvider<IValueProvider>() is null
            && n.Peer.GetProvider<ISelectionItemProvider>() is null;
    }

    public static string Tree(AutomationPeer root, int maxDepth, bool includeHidden, bool includeLayout)
    {
        var sb = new StringBuilder();
        // Walk is depth-first, so the entry for depth d-1 always belongs to the current node's parent:
        // indent = 1 + the indent of the closest printed ancestor (folded layout nodes don't indent).
        var indentAt = new int[1024];
        foreach (var n in Walk(root, includeHidden))
        {
            if (n.Depth > maxDepth || n.Depth >= indentAt.Length) continue;
            int parentIndent = n.Depth == 0 ? -1 : indentAt[n.Depth - 1];
            bool show = n.Depth == 0 || includeLayout || !IsNoise(n);
            int indent = show ? parentIndent + 1 : parentIndent;
            indentAt[n.Depth] = indent;
            if (show) sb.Append(' ', indent * 2).AppendLine(Line(n.Info));
        }
        return sb.ToString();
    }

    // ---- actions -----------------------------------------------------------------------------

    /// <summary>Performs <paramref name="action"/> (or the element's natural one) and says what it did.</summary>
    public static string Act(UiNode n, string action)
    {
        var p = n.Peer;
        // Avalonia's MenuItem peer exposes no invoke/expand pattern, so drive menu items directly.
        if (n.Control is MenuItem mi && action.ToLowerInvariant() is "default" or "invoke" or "expand" or "collapse")
            return ActOnMenuItem(mi, action.ToLowerInvariant());
        switch (action.ToLowerInvariant())
        {
            case "invoke": Require(p.GetProvider<IInvokeProvider>(), "invoke").Invoke(); return "invoked";
            case "toggle": Require(p.GetProvider<IToggleProvider>(), "toggle").Toggle(); return "toggled";
            case "select": Require(p.GetProvider<ISelectionItemProvider>(), "select").Select(); return "selected";
            case "expand": Require(p.GetProvider<IExpandCollapseProvider>(), "expand").Expand(); return "expanded";
            case "collapse": Require(p.GetProvider<IExpandCollapseProvider>(), "collapse").Collapse(); return "collapsed";
            case "focus": p.SetFocus(); return "focused";
            case "contextmenu": p.ShowContextMenu(); return "context menu shown";
            case "default":
                if (p.GetProvider<IInvokeProvider>() is { } inv) { inv.Invoke(); return "invoked"; }
                if (p.GetProvider<IToggleProvider>() is { } tog) { tog.Toggle(); return "toggled"; }
                if (p.GetProvider<ISelectionItemProvider>() is { } sel) { sel.Select(); return "selected"; }
                if (p.GetProvider<IExpandCollapseProvider>() is { } ec)
                {
                    if (ec.ExpandCollapseState == ExpandCollapseState.Expanded) { ec.Collapse(); return "collapsed"; }
                    ec.Expand(); return "expanded";
                }
                throw new UiAutomationException(
                    $"'{n.Info.Name}' ({n.Info.Type}) has no invoke/toggle/select/expand pattern; try action 'focus' and ui_key.");
            default:
                throw new UiAutomationException(
                    $"Unknown action '{action}'. Use default, invoke, toggle, select, expand, collapse, focus or contextmenu.");
        }
    }

    private static string ActOnMenuItem(MenuItem mi, string action)
    {
        if (action == "collapse") { mi.Close(); return "collapsed"; }
        if (mi.ItemCount > 0)
        {
            if (mi.Parent is Menu bar && !bar.IsOpen) bar.Open();
            mi.Open();
            return "expanded";
        }
        if (action == "expand") throw new UiAutomationException("The menu item has no submenu.");
        mi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, mi));
        return "invoked";
    }

    private static T Require<T>(T? provider, string what) where T : class =>
        provider ?? throw new UiAutomationException($"The element does not support '{what}'.");

    public static string SetText(UiNode n, string text)
    {
        if (n.Peer.GetProvider<IValueProvider>() is { IsReadOnly: false } v) { v.SetValue(text); return "value set"; }
        var prop = n.Control?.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
        if (prop is { CanWrite: true } && prop.PropertyType == typeof(string)) { prop.SetValue(n.Control, text); return "Text set"; }
        throw new UiAutomationException($"'{n.Info.Name}' ({n.Info.Type}) has no editable text.");
    }

    /// <summary>The element keyboard input goes to: the given one (focused first) or the active window's focus.</summary>
    public static IInputElement KeyboardTarget(UiNode? target)
    {
        if (target?.Control is { } c)
        {
            c.Focus();
            return c;
        }
        var window = Windows().FirstOrDefault(w => w.IsActive) ?? Windows().FirstOrDefault()
            ?? throw new UiAutomationException("No window is open.");
        return window.FocusManager?.GetFocusedElement() ?? window;
    }

    /// <summary>Raises KeyDown/KeyUp for a gesture like "Ctrl+S", "Escape" or "Ctrl+Shift+F".</summary>
    public static void Key(IInputElement target, string gesture)
    {
        KeyGesture g;
        try { g = KeyGesture.Parse(gesture); }
        catch (Exception ex) { throw new UiAutomationException($"Can't parse key gesture '{gesture}': {ex.Message}"); }
        foreach (var ev in new[] { InputElement.KeyDownEvent, InputElement.KeyUpEvent })
        {
            target.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = ev,
                Key = g.Key,
                KeyModifiers = g.KeyModifiers,
                Source = target,
            });
        }
    }

    /// <summary>Raises a TextInput event, which is how text boxes and the editor receive typed characters.</summary>
    public static void Type(IInputElement target, string text) =>
        target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = target });

    // ---- reading -------------------------------------------------------------------------------

    /// <summary>Reads a dotted property path off the element's control, e.g. "IsChecked" or "DataContext.Status".</summary>
    public static string ReadPath(Control control, string path)
    {
        object? cur = control;
        foreach (var seg in path.Split('.'))
        {
            if (cur is null) return "null";
            var prop = cur.GetType().GetProperty(seg, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null) return $"<no property '{seg}' on {cur.GetType().Name}>";
            cur = prop.GetValue(cur);
        }
        return Describe(cur);
    }

    private static string Describe(object? v) => v switch
    {
        null => "null",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable e => $"[{string.Join(", ", e.Cast<object?>().Take(50).Select(x => x?.ToString() ?? "null"))}]",
        _ => v.ToString() ?? v.GetType().Name,
    };

    public static byte[] RenderPng(Visual visual)
    {
        var scale = TopLevel.GetTopLevel(visual)?.RenderScaling ?? 1.0;
        var size = visual.Bounds.Size;
        if (size.Width < 1 || size.Height < 1) throw new UiAutomationException("The element has no size to render.");
        var px = new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
        using var rtb = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
        rtb.Render(visual);
        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>An automation request that can't be carried out; its message goes back to the caller as is.</summary>
public class UiAutomationException(string message) : Exception(message);

/// <summary>Nothing matched (yet); callers retry these for a short while, like Playwright's auto-wait.</summary>
public sealed class UiElementNotFoundException(string message) : UiAutomationException(message);
