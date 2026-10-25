# Numele stațiilor, caracterul `.` și parsarea numelor calificate

**Stare:** implementat · **Dată:** 2026-07-01 · **Commit de corectare:** `2913fd2` ("fix(semantics): allow '.' in station names (e.g. N32.11)")

Această notă consemnează de ce există corectura și invarianții pe care modificările viitoare trebuie
să-i păstreze, pentru ca bug-ul să nu fie reintrodus când codul de denumire/rezolvare este modificat din nou.

## Problema

În Therion, **numele stațiilor pot conține `.`** — de ex. `N32.11`, `N32.23` și chiar nume cu mai multe
puncte, precum `N32.11.i`. Exemplu real (fișierul care a scos problema la iveală):
`tests/Corpus/sample_projects/Therion_202502x/lox/Cerna_lox.th`

```
# declaration (sub-file PS-6nord/.../20181103_ps6b_lox.th, inside `survey SV-ps6b`)
N32.10 N32.11 2.87 225.9 -27.7      # from = N32.10, to = N32.11  (dots are literal)

# cross-survey link (Cerna_lox.th)
equate N32.11@SV-ps6b N32.11@SV-ps6f_labirint_Raul_batran
```

Binder-ul interpreta greșit stația from/to cu puncte `N32.11` drept **survey `N32` + stația `11`**,
indexa stația sub cheia greșită, iar apoi `equate`-ul cu `@` (care păstrează corect `N32.11` întreg)
nu o mai găsea — producând avertismente false `TH_SEM_001` „referință de stație nerezolvată”.

Cauza principală a fost o singură presupunere supraîncărcată: **`.` era tratat întotdeauna ca separator
de ierarhie de survey-uri**, deși în interiorul unui token de stație este un caracter literal.

## Regula de denumire Therion (pe care o urmăm acum)

- Un **nume de stație este un token opac**. Punctele din interiorul lui sunt literale.
- **Ierarhia de survey-uri** se exprimă doar prin:
  1. **imbricarea survey-urilor** (domeniul `survey … endsurvey` care o cuprinde) și
  2. **notația `@`** — `station@inner.outer` (de jos în sus). Partea **din stânga lui `@` este numele
     stației** (poate conține puncte); partea **din dreapta lui `@` este calea de survey-uri** (separată
     prin puncte, ierarhică). `StationRef` parsează aceasta și inversează calea de survey-uri în ordine
     de sus în jos.
- Therion **nu** folosește în sintaxa sursă o referință de stație cu puncte de sus în jos
  (`survey.survey.station`); referințele între survey-uri folosesc `@`. (Păstrăm totuși o variantă de
  rezervă pentru ea — vedeți mai jos.)

## Reprezentarea internă și invarianții

`QualifiedName` conține `ImmutableArray<string> Parts` — componente **deja separate**, **ultima dintre
ele fiind numele întreg al stației** (care poate conține puncte). Tabloul de părți este neambiguu;
pericolul apare doar la *reconstruirea* lui dintr-un șir prin împărțire după `.`.

**Invarianți — nu îi încălcați:**

1. **Nu împărțiți niciodată un token brut de stație după `.`.** Pentru a califica un token de stație
   față de un domeniu de survey, folosiți `QualifiedName.OfStation(scope, name)` (adaugă tokenul întreg).
   **Nu** folosiți `QualifiedName.Parse` pentru aceasta.
2. `QualifiedName.Parse(dotted)` **chiar** împarte după fiecare `.`. Este doar pentru căi de survey-uri
   și pentru șiruri cunoscute cu puncte, fără componente de stație cu puncte (conversii interne dus-întors,
   teste). Documentația sa XML conține acest avertisment.
3. Lexerul păstrează deja întregi tokenurile cu puncte — `.` **nu** este separator pentru tokenizer.
   Păstrați lucrurile așa.
4. Rezolvarea între survey-uri trece prin `StationRef` (parserul pentru `@`), care păstrează punctul
   întreg. Indexul principal, **neambiguu**, este `StationsBySurveyAndPoint`, cu cheia
   `(immediate-survey-last, whole-point)`. `StationsByQn` (cheia completă cu puncte) este o variantă de
   rezervă și este teoretic ambiguu pentru numele de stații cu puncte — nu vă bazați niciodată pe el ca
   unic mecanism de rezolvare.

## Ce a schimbat corectura

Totul în `src/Therion.Semantics/`:

- **`SemanticBinder.QualifyLocal`** — păstrează tokenul de stație întreg prin noul factory
  `QualifiedName.OfStation(scope, name)` în loc de `if (token.Contains('.')) Parse(token)`.
  Aceasta este corectura esențială (partea de declarare).
- **`SemanticBinder.TryResolveRef`** — rezolvă **întâi tokenul întreg** și recurge la vechea împărțire
  a căii după puncte (`raw.Split('.')`) doar dacă tokenul întreg nu se rezolvă nicăieri. Astfel,
  `equate`-urile cu puncte din același fișier se rezolvă, iar orice referință ipotetică cu puncte de sus
  în jos funcționează în continuare (fără regresie).
- **`QualifiedName`** — noul factory `OfStation` (care documentează regula) și un avertisment în
  documentația lui `Parse`.

Neatinse: tokenizerul, AST-ul, `StationRef` (tratarea lui `@` era deja corectă) și reprezentarea
`QualifiedName`.

## Riscuri / de urmărit

- O referință **cu puncte de sus în jos, fără `@`** (`equate cave.upper.1 …`) este acum citită întâi ca
  nume literal. Dovezile (corpusul + notația `@` din thbook) arată că această formă nu este folosită în
  sursele Therion, iar varianta de rezervă din `TryResolveRef` o păstrează dacă apare vreodată.
- Dacă o funcționalitate viitoare adaugă o sintaxă de referință cu puncte cu adevărat ierarhică,
  rezolvați-o explicit în acel loc — **nu** reintroduceți împărțirea generală după `.` în
  `QualifyLocal` sau `QualifiedName`.

## Teste

`tests/Therion.Semantics.Tests/DottedStationNameTests.cs`:
- `Station_name_with_dot_is_kept_whole` — declarația are cheia `[survey, "N32.11"]`, nu `["N32","11"]`.
- `Same_file_equate_of_dotted_station_resolves` — acoperă rezolvarea „întâi tokenul întreg” din `TryResolveRef`.
- `Cross_survey_at_equate_of_dotted_station_resolves` — cazul raportat de `equate` cu `@`.
- `Plain_station_names_are_unaffected` — comportamentul pentru `survey.station` fără puncte rămâne neschimbat.

Verificat pe corpusul real (`thconfig_Cerna_lox.thc`): avertismentele de stație nerezolvată pentru
`N32.11` / `N32.23` dispar, iar numele cu mai multe puncte (`N32.11.i`) se rezolvă. `TH_SEM_012` rămas
acolo este o vizare reală de lungime zero din date, fără legătură cu această corectură.
