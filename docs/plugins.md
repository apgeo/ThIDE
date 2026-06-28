# Plugins (EXT-04)

TherionProc loads external **semantic-rule** plugins at startup, so you can add custom diagnostics
without rebuilding the app.

## Writing a plugin

Create a .NET class library that references `Therion.Semantics` and implements `ISemanticRule`:

```csharp
using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;

public sealed class MyRule : ISemanticRule
{
    public string Id => "MY-001";

    public ImmutableArray<Diagnostic> Run(SemanticContext ctx)
    {
        // Inspect ctx.Model (stations, shots, surveys, …) and return diagnostics.
        return ImmutableArray<Diagnostic>.Empty;
    }
}
```

Requirements for discovery:
- a **public** non-abstract type implementing `ISemanticRule`,
- a **public parameterless constructor**.

## Installing

Drop the compiled `*.dll` into:

```
%AppData%/TherionProc/plugins      (Windows)
~/.config/TherionProc/plugins      (Linux/macOS, XDG fallback)
```

Plugins are scanned at startup and their rules run alongside the built-ins (results appear in the
Diagnostics panel). A plugin that fails to load is logged and skipped — it never blocks startup.

## Performance

Plugin rules run during semantic analysis, so they add processing time. For very large projects you
can turn plugins off in **Preferences ▸ Extensions ▸ Load plugins** (changes apply on restart).

## Other extension points

- **Custom semantic rules via config** — naming-convention lints can be declared in `rules.json`
  (no assembly needed); see LANG-13.
- **Headless tooling** — the `therion-cli` (EXT-01) and `therion-lsp` (EXT-05) executables expose the
  same engines for CI / other editors.
