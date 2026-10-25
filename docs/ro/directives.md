# Directive de aplicație (`#@`)

> [Index documentație](README.md) · [README-ul proiectului](../../README.md)

ThIDE adaugă un strat pentru editor/UX care trăiește **în interiorul comentariilor Therion**, așa că este pe deplin
compatibil înainte/înapoi cu sintaxa Therion — Therion vede doar un comentariu și îl ignoră.
Aceste *directive de aplicație* sunt echivalentul aproximativ al liniilor de preprocesor / pragma. Primul
(și deocamdată singurul) consumator este blocul pliabil **`#@region … #@endregion`**; o etapă viitoare
adaugă `#@if/#@elif/#@else/#@endif` pe același model.

Motorul este o bibliotecă pură — [`src/Therion.Syntax/Directives/`](../../src/Therion.Syntax/Directives) —
deci este reutilizabil și testat unitar independent de aplicație.

## Sintaxă

```
#@<type> <arg1> <arg2> … <argN>
```

- `#@` deschide o directivă; **tipul** urmează imediat și **nu ține cont de majuscule/minuscule**
  (`#@Region` ≡ `#@region`).
- Un `#` în interiorul unui șir Therion între ghilimele duble nu este un comentariu, deci nu începe o directivă
  (la fel ca tokenizer-ul).
- **Argumentele** sunt separate prin șiruri de spații și/sau o singură virgulă (cu spații opționale în jurul
  ei). O virgulă poate delimita un loc *gol* → acel argument este **nedefinit**; un șir de spații
  nu produce niciodată un argument gol, iar o virgulă la final nu adaugă niciunul.
- Un argument scris `_` sau `undefined` (fără a ține cont de majuscule/minuscule) este **nedefinit**.
- Un argument încadrat de `'…'` sau `"…"` este un singur argument egal cu textul citat, întocmai
  (un `undefined` între ghilimele este o valoare definită).

## Regiuni (`#@region` / `#@endregion`)

Încadrați orice porțiune dintr-un fișier `.th` / `.th2` / `.thconfig` într-o regiune pliabilă, cu nume:

```therion
#@region "Entrance series"
  centreline
    ...
  endcentreline
#@endregion
```

- Primul argument, opțional, este **titlul** regiunii, afișat pe antetul pliat (un
  `#@region` fără argumente se pliază ca `#@region`).
- Regiunile se **imbrică** precum `#region` din C#: cel mai interior `#@endregion` închide cel mai interior
  `#@region` deschis. Doar regiunile *închise* sunt pliabile.
- Plierea este calculată de [`TherionFoldingStrategy`](../../ThIDE/Editor/TherionFoldingStrategy.cs) pe baza
  aceluiași [`DirectiveScanner`](../../src/Therion.Syntax/Directives/DirectiveScanner.cs) pe care îl folosesc diagnosticele,
  astfel încât plierea și avertismentele nu sunt niciodată în dezacord asupra locului unde se termină o regiune.

### Include în regiune

Selectați câteva linii și rulați **Editare → Include în regiune** (`Ctrl+Shift+R`, disponibil și în paleta de comenzi).
Vi se cere un titlu; selecția este încadrată în `#@region "<title>"` / `#@endregion`. Punerea titlului
între ghilimele este gestionată de [`RegionDirective`](../../src/Therion.Syntax/Directives/RegionDirective.cs)
(preferă ghilimelele simple și trece la ghilimele duble când titlul conține una).

## Diagnostice

Regiunile neechilibrate sunt semnalate în panoul Diagnostice — vedeți
[diagnostics.md](diagnostics.md#directive-ale-aplicației-thide_dir_xxx):

| Cod | Semnificație |
|---|---|
| `THIDE_DIR_001` | `#@region` nu are `#@endregion` corespunzător. |
| `THIDE_DIR_002` | `#@endregion` nu are `#@region` corespunzător. |
