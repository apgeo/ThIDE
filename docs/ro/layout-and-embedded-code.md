# Blocuri layout și cod încorporat (MetaPost / TeX)

Cum analizează și evidențiază aplicația un bloc Therion `layout … endlayout` și codul în alt limbaj
încorporat în el.

## Ce conține corpul unui layout

Corpul unui `layout` este un amestec de trei lucruri:

1. **Linii de opțiuni** — `key [value…]` (de ex. `scale 1 500`, `legend on`, `symbol-hide point cave-station`).
2. **MetaPost încorporat** — `code metapost … endcode`. Macro-uri MetaPost care personalizează desenarea.
3. **TeX încorporat** — `code tex-map … endcode` și `code tex-atlas … endcode`. TeX/LaTeX pentru
   textul legendei/hărții/atlasului.

`code metapost` și `code tex-*` sunt **limbaje diferite** — MetaPost este un limbaj de desenare
derivat din Metafont; `tex-map`/`tex-atlas` sunt TeX. Ele sunt evidențiate de lexere separate.

## Parsare

Blocurile layout sunt parsate într-un `LayoutCommand` tipizat
([`src/Therion.Syntax/ThconfigAst.cs`](../../src/Therion.Syntax/ThconfigAst.cs)) de
[`LayoutBodyParser`](../../src/Therion.Syntax/LayoutBodyParser.cs), refolosit atât de parserul
`.thconfig` ([`ThconfigParser`](../../src/Therion.Syntax/ThconfigParser.cs)), cât și de parserul `.th`
([`ThParser`](../../src/Therion.Syntax/ThParser.cs)) — unele proiecte țin layout-urile în fișiere `.th`
incluse cu `input`. Modelul reține opțiunile, directivele `copy`/`cs`/`symbol-set`/`symbol-*` și
blocurile încorporate `code … endcode` (înregistrate cu eticheta lor de limbaj, corpul fiind păstrat opac).

Cheile de opțiuni necunoscute generează `UnknownLayoutOption` (avertisment în modul permisiv, eroare
în modul strict). Cheile cunoscute se află în [`LayoutKeywords`](../../src/Therion.Syntax/LayoutKeywords.cs).

### Regula lacomă `code` → `endcode`

Therion citește un bloc `code` în mod lacom (greedy) până la **următorul** `endcode`. Un
`code metapost` fără `endcode` înghite așadar liniile de opțiuni care urmează, până la următorul
`endcode`. Acest lucru apare în practică: vedeți
[`tests/Corpus/Synthetic/project/Vladusca.thconfig`](../../tests/Corpus/Synthetic/project/Vladusca.thconfig),
unde primul `code metapost` nu are `endcode` și trage un `scale 1 100` ulterior în blocul metapost.
Atât parserul (`LayoutBodyParser`), cât și scannerul de regiuni al editorului respectă această regulă,
astfel încât cele două nu sunt niciodată în dezacord asupra locului unde se termină un bloc de cod.

## Evidențiere

În trecut, editorul lăsa necolorat întregul corp al layout-ului. Acum fiecare linie este clasificată
într-un `EmbeddedRegion` de [`LayoutRegionScanner`](../../src/Therion.Syntax/LayoutRegionScanner.cs) —
sursa unică de adevăr pentru „în ce limbaj este această linie?”:

| Regiune | Linii | Evidențiator |
|---|---|---|
| `LayoutOption` | linii de opțiuni + delimitatorii `code …`/`endcode` | `TokenClassifier.ClassifyLayoutLine` (cheie de opțiune → cuvânt cheie) |
| `MetaPost` | corpul unui bloc `code metapost` | [`MetaPostLexer`](../../src/Therion.Syntax/MetaPostLexer.cs) |
| `Tex` | corpul unui bloc `code tex-map`/`tex-atlas` | [`TexLexer`](../../src/Therion.Syntax/TexLexer.cs) |
| `None` | corpuri opace (de ex. `lookup`) | neevidențiat |
| _absent_ | linii Therion obișnuite (incl. `layout`/`endlayout`) | `TokenClassifier.Classify` |

[`TherionColorizer`](../../ThIDE/Editor/TherionColorizer.cs) primește harta prin
`SetLineRegions` și face dispatch pe fiecare linie; toate lexerele emit fluxul comun `ClassifiedSpan` /
`TokenClassification`, astfel încât sunt redate prin aceeași paletă (și prin aceleași căi pentru
raportul HTML / CLI), fără culori noi.

### Lexere încorporate — domeniu

`MetaPostLexer` și `TexLexer` sunt tokenizere **de nivelul evidențierii**, nu parsere complete — nu
există un parser .NET matur și reutilizabil pentru niciunul dintre limbaje (ambele implementări de
referință sunt C/web2c), iar noi avem nevoie doar de colorare lexicală. Ele recunosc comentarii,
șiruri, numere, operatori/grupări și un set selectat de cuvinte cheie/primitive (plus macro-uri
MetaPost specifice Therion, cum ar fi `thdraw`/`thfill`). Lucrează la nivel de linie, la fel ca
colorizatorul Therion; un șir care se întinde pe mai multe linii `code` (foarte rar) ar fi evidențiat
greșit până când un viitor evidențiator incremental îl va trata.

## Lucrări viitoare (amânate, intenționat în afara domeniului)

- **Validare detaliată a valorilor fiecărei opțiuni.** Astăzi se validează doar *cheia* opțiunii;
  forma argumentelor nu. O etapă viitoare ar putea valida valorile (de ex. `scale` → numere pozitive,
  `legend` → on/off, `color-model` → cmyk/rgb/grey, `symbol-set` → un standard cunoscut,
  `north` → true/grid, `units` → unitate + factor). Vedeți blocul `FUTURE` din
  [`LayoutKeywords.cs`](../../src/Therion.Syntax/LayoutKeywords.cs) pentru lista completă a celor amânate.
  Aceasta a fost amânată pentru a păstra suprafața mică și a evita fragilitatea între versiunile Therion.
- **Construcții încorporate pe mai multe linii** (șiruri care se întind pe linii `code`) printr-un
  evidențiator incremental.
- **Completare sensibilă la context** în corpul unui layout (oferirea `LayoutKeywords.Keys` +
  `SymbolSets.StandardNames`).
