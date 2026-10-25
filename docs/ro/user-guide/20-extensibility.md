# 20. Extensibilitate (CLI, LSP, asistenți AI, automatizarea interfeței, pluginuri, cârlige)

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Pentru utilizatorii avansați, felul în care ThIDE înțelege Therion este disponibil și *în afara*
interfeței grafice și poate fi extins. Nimic din toate acestea nu este necesar în utilizarea de zi cu
zi — dar există atunci când doriți să automatizați sau să integrați.

## Instrumentul de linie de comandă (`therion-cli`)

Un instrument **fără interfață** (headless) care rulează aceeași analiză și interpretare ca aplicația —
ideal pentru scripturi, CI sau o verificare rapidă fără a deschide interfața grafică:

```sh
therion-cli validate path/to/project.thconfig   # raportează diagnosticele
therion-cli lint     path/to/file.th             # verificări de stil / convenții
therion-cli format   path/to/file.th --write     # reindentare pe loc
therion-cli stats    path/to/project.thconfig    # lungime/adâncime etc.
therion-cli deps     path/to/project.thconfig --dot   # graful de includeri (Graphviz)
therion-cli gis      path/to/project.thconfig --format kml --out entrances.kml
```

Există și `dump-ast` și `list-stations`. Lista completă de comenzi și exemple:
[docs/usage.md → Instrumente de linie de comandă](../usage.md#unelte-de-linie-de-comandă).

## Serverul de limbaj (`therion-lsp`)

Un **Language Server independent de editor**, care oferă diagnostice Therion prin stdio — configurați
orice editor compatibil LSP (VS Code, Neovim, …) să îl folosească și veți avea verificările ThIDE în
editorul preferat. Instalare și configurarea clientului: **[docs/lsp.md](../lsp.md)**.

## Asistenți AI (MCP)

> **Experimental — în lucru.** Integrarea MCP și panoul **Asistent** sunt recente și încă se schimbă.
> Așteptați-vă la asperități și la setări sau comportamente care se modifică de la o versiune la alta.

ThIDE poate permite unui **asistent AI local** să răspundă la întrebări despre proiectul dumneavoastră —
*„ce nu e în regulă cu această peșteră?”*, *„este totul o singură bucată conectată?”*, *„unde este
declarată stația 12?”* — fără să lipiți fișiere într-o fereastră de chat. Face acest lucru vorbind
**Model Context Protocol (MCP)**, la fel cum instrumente precum Claude sau un model local din
**LM Studio** comunică cu uneltele lor.

Trei moduri de utilizare:

- **Panoul Asistent integrat (nicio configurare în afară de un model local).** **Vizualizare →
  Asistent** deschide un chat în ThIDE care comunică cu un model local servit de **LM Studio** (sau cu
  orice endpoint compatibil OpenAI — setați-l în **Preferințe → MCP**). Răspunde pe baza proiectului
  deschis, iar fiecare modificare pe care o propune se oprește mai întâi la un card **Permite /
  Refuză** — nimic nu este scris până nu permiteți.
- **Pe fișierele de pe disc (fără ThIDE).** Un mic program, `therion-mcp`, rulează motoarele ThIDE
  fără interfață. Gazda AI (LM Studio, Claude Code, …) îl lansează și poate apoi pune întrebări despre
  un dosar de proiect. Citește fișierele dumneavoastră; poate de asemenea să formateze, să genereze
  structuri, să importe/exporte și să ruleze Therion — sau, dacă preferați, poate fi limitat la
  **doar citire** cu un singur comutator.
- **Pe aplicația pe care o aveți deschisă.** Activați **Preferințe → MCP → „Activează serverul de
  unelte AI din aplicație (MCP)”**, iar ThIDE-ul *care rulează* oferă aceleași capabilități plus
  unele în timp real: asistentul poate vedea fișierul pe care îl editați (inclusiv modificările
  nesalvate), poate deschide un fișier, focaliza un panou sau arăta o stație în vizualizarea 3D. Dacă
  are voie să *atingă* fereastra — nu doar să o citească — decide caseta **„Urmează agentul”** de
  lângă această setare; dezactivați-o ca asistentul să nu vă modifice aranjamentul, permițându-i în
  continuare să răspundă la întrebări.

**Este sigur?** Serverul din aplicație ascultă doar pe propriul calculator (loopback), în spatele unei
parole aleatorii, și este **dezactivat implicit**. Un asistent nu poate ieși niciodată din dosarul
proiectului, iar o editare a unui fișier pe care îl editați chiar acum (cu modificări nesalvate) este
refuzată, în loc să vă suprascrie munca. Dacă sunteți precaut, porniți-l în modul doar citire.

Configurarea unei anumite gazde AI (LM Studio, Claude Code) înseamnă câteva rânduri de configurație —
vedeți pașii în **[docs/mcp-host-setup.md](../mcp-host-setup.md)**.

## Automatizarea interfeței (pentru teste și agenți de programare)

ThIDE poate fi controlat din exterior — clicuri, tastare, citirea a ce apare pe ecran, capturi de
ecran — pentru teste automate ale interfeței sau pentru un agent de programare care trebuie să își
verifice modificarea în aplicația reală. Aceasta este o **funcție pentru dezvoltatori**: nimic din
aplicație nu o activează. Pornește doar când ThIDE este lansat cu variabila de mediu
`THIDE_UI_AUTOMATION=1`, ascultă doar pe propriul calculator și necesită un token aleatoriu. Este
separată de serverul de unelte AI de mai sus.

Cum funcționează, suitele de teste și utilitarul `uidrive`: **[docs/dev/ui-testing.md](../dev/ui-testing.md)**.

## Pluginuri de reguli semantice

Puteți adăuga **propriile reguli de validare** fără a recompila ThIDE:

1. Scrieți un mic tip .NET care implementează **`ISemanticRule`**.
2. Puneți fișierul `.dll` compilat în `%AppData%/ThIDE/plugins`.
3. Activați **Încarcă pluginuri** în [Setări → Extensii](19-settings-and-preferences.md#extensii) și
   reporniți.

Regula dumneavoastră contribuie apoi cu diagnostice la fel ca cele integrate. API-ul și exemple:
**[docs/plugins.md](../plugins.md)**. Regulile pot fi configurate și printr-un `rules.json`.

## Cârlige de script

Rulați o comandă externă în momentele-cheie — **La deschidere**, **La salvare** sau **La compilare**:

1. Activați **cârligele de script** în [Setări → Extensii](19-settings-and-preferences.md#extensii).
2. Setați comanda pentru fiecare eveniment; folosiți **`{file}`** pentru calea fișierului (thconfig-ul
   activ, în cazul compilării).

Folosiți-le pentru a declanșa o copie de rezervă, un post-procesor personalizat, o notificare sau un
commit în sistemul de versionare. Dezactivați-le pe spațiile de lucru foarte mari dacă încetinesc
lucrurile.

## Unde se află lucrurile

- Pluginuri: `%AppData%/ThIDE/plugins/*.dll`
- Configurația regulilor: `rules.json` (datele aplicației)
- CLI-ul și LSP-ul sunt **executabile separate** livrate alături de aplicație, nu interfața grafică
  propriu-zisă.

Vedeți și documentul pentru dezvoltatori [docs/architecture.md](../architecture.md) dacă doriți să
reutilizați bibliotecile de bază în propriul program .NET.

---

Următorul: [Scurtături tastatură →](21-keyboard-shortcuts.md)
