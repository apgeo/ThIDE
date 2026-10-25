# Pluginuri (EXT-04)

ThIDE încarcă la pornire pluginuri externe de **reguli semantice**, astfel încât puteți adăuga
diagnostice personalizate fără a recompila aplicația.

## Scrierea unui plugin

Creați o bibliotecă de clase .NET care referențiază `Therion.Semantics` și implementează `ISemanticRule`:

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

Cerințe pentru a fi descoperit:
- un tip **public**, neabstract, care implementează `ISemanticRule`,
- un **constructor public fără parametri**.

## Instalare

Copiați fișierul `*.dll` compilat în:

```
%AppData%/ThIDE/plugins      (Windows)
~/.config/ThIDE/plugins      (Linux/macOS, XDG fallback)
```

Pluginurile sunt scanate la pornire, iar regulile lor rulează alături de cele încorporate (rezultatele
apar în panoul Diagnostice). Un plugin care nu se încarcă este înregistrat în jurnal și ignorat — nu
blochează niciodată pornirea.

## Performanță

Regulile din pluginuri rulează în timpul analizei semantice, deci adaugă timp de procesare. Pentru
proiecte foarte mari puteți dezactiva pluginurile din **Preferințe ▸ Extensii ▸ Încarcă pluginuri**
(modificările se aplică după repornire).

## Alte puncte de extensie

- **Reguli semantice personalizate prin configurare** — verificările de convenții de denumire pot fi
  declarate în `rules.json` (nu este nevoie de un assembly); vedeți LANG-13.
- **Unelte fără interfață (headless)** — executabilele `therion-cli` (EXT-01) și `therion-lsp` (EXT-05)
  expun aceleași motoare pentru CI / alte editoare.
