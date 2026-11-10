# 8. Navigare și căutare

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Pentru că ThIDE înțelege întregul proiect ([graful de obiecte](04-core-concepts.md#graful-de-obiecte-modelul-semantic)),
vă poate purta prin el după *sens*, nu doar după text. Aici se câștigă mult timp.

## Paleta de comenzi

**Ctrl+Shift+P** deschide Paleta de comenzi — o singură casetă de căutare pentru orice. Începeți să
tastați numele unei comenzi (Compilează, Comută Diagnosticele, Formatează documentul, Comută la
română…); fiecare comandă își arată în dreapta scurtătura curentă. Comenzile sunt grupate pe categorii
(Fișier, Compilare, Vizualizare, Navigare, Căutare, Editare, Editor, Setări, Documentație, …).

Paleta servește și drept casetă de salt, prin **prefixe**:

| Tastați | Efect |
|---|---|
| *(text)* | Potrivire aproximativă a unei comenzi de executat |
| `@name` | Salt la un simbol **din documentul curent** |
| `#name` | Salt la un simbol **din întregul spațiu de lucru** (topografii, stații, schițe, hărți) |
| `:42` | Salt la **linia 42** a fișierului curent |

## Deschiderea fișierelor și comutarea între ele

| Acțiune | Scurtătură | Observații |
|---|---|---|
| **Salt la fișier** | **Ctrl+P** | Deschidere de fișier cu potrivire aproximativă. Listează mai întâi fișierele thconfig-ului activ; celelalte fișiere Therion din spațiul de lucru urmează, estompate și marcate „(nu este în *nume*.thconfig)”, sau sunt omise cu [Setări → Spațiu de lucru](19-settings-and-preferences.md#spațiu-de-lucru). |
| Comutare între documentele deschise | **Ctrl+Tab** | Parcurge pe rând filele deschise. |
| **Redeschide fila închisă** | **Ctrl+Shift+T** | **Fișier → Redeschide fila închisă**; readuce ultimul document închis. |
| Afișarea fișierului activ în arbore | — | Paletă de comenzi → *Afișează fișierul activ în Exploratorul spațiului de lucru*. |

## Salt la simbol

- **Salt la simbol în document** și **Salt la simbol în spațiul de lucru** (butoane din bara de
  instrumente, Paleta de comenzi sau prefixele `@` / `#`). Caută în topografii, stații, schițe și
  hărți.
- Peșteri foarte mari: numărul de stații listate este limitat, pentru ca aplicația să rămână
  receptivă ([Setări → Performanță](19-settings-and-preferences.md#performanță)).

## Salt după sens

Dintr-un identificator din editor (cardul informativ sau clic dreapta):

| Acțiune | Scurtătură | Ce face |
|---|---|---|
| **Salt la definiție** | **F12** / clic / Ctrl+clic / dublu-clic* | Sare la locul unde este declarată o stație/topografie/un fișier. |
| **Salt la echivalare** | — | Urmează un `equate` până la stația legată. |
| **Salt la harta care îl agregă** | — | De la o schiță/hartă, sare la harta care o include. |
| **Găsește referințele** | **Shift+F12** | **Editare → Găsește referințele**, meniul contextual sau cardul de la trecerea cu mouse-ul: deschide o listă cu fiecare folosire a simbolului în tot proiectul (înțelege stațiile și topografiile și răspunde și pentru un cuvânt oarecare); bara de stare vă spune când nu există altă folosire. |
| **Următoarea apariție în acest fișier** | **Ctrl+Shift+F12** | Mută pe rând cursorul prin celelalte locuri din acest fișier unde apare același cuvânt. |
| **Vezi definiția** | **Alt+F12** | Vedeți definiția în linie, fără să vă părăsiți locul. |
| **Salt la blocul pereche** | **Ctrl+]** | Între începutul unui bloc și `end…`-ul său. |
| **Intră în fișierul inclus** | **Alt+↓** | Deschide fișierul spre care trimite o linie `input`. |

\* Dacă un singur clic navighează sau doar plasează cursorul este o preferință
(**Necesită dublu-clic pentru a merge la definiție**) — Ctrl+clic navighează întotdeauna. Vedeți
[Setări → Editor](19-settings-and-preferences.md#editor).

## Istoricul cursorului: Înapoi și Înainte

**Alt+Left** / **Alt+Right** (și săgețile din bara de instrumente) parcurg istoricul
cursorului/navigării — săriți la o definiție, apoi reveniți direct unde erați, chiar și între fișiere.

## Redenumirea unui simbol peste tot

**F2** (**Editare → Redenumește simbolul…** sau *Redenumește simbolul* din cardul de la trecerea cu
mouse-ul / din paletă) redenumește o stație/topografie **în fiecare fișier care o referă**. Dacă este
activată, o **previzualizare** listează mai întâi aparițiile (numărul lor în N fișiere), fiecare ca
linia cu vechiul nume tăiat și cel nou îngroșat, pe care le puteți inspecta cu dublu-clic înainte de a
aplica. Previzualizarea se activează/dezactivează în
[Setări → Editor](19-settings-and-preferences.md#editor). Stațiile pot fi redenumite și din grila
Măsurători (clic dreapta → *Redenumește stația…*).

**Ce acoperă anularea.** Un fișier deschis primește redenumirea în editorul său, ca un singur pas de
anulare al acelui fișier. Un fișier care **nu este deschis este rescris pe disc**, fără un editor în
care să fie anulat — așa că redenumirea în ansamblu este înregistrată în
[anularea la nivelul spațiului de lucru](05-interface-tour.md#anularea-unei-unelte-care-a-scris-în-mai-multe-fișiere):
**Editare → Anulează modificarea din mai multe fișiere** (după o redenumire apare ca *Anulează:
Redenumire*) readuce fiecare fișier. Dacă un fișier nu poate fi scris, nu se redenumește nimic, iar
mesajul îl numește.

## Căutare și înlocuire

- **În fișierul curent:** **Căutare → Caută** și **Căutare → Înlocuiește** — cu opțiunile de potrivire
  a majusculelor, cuvânt întreg și expresii regulate.
- **În mai multe fișiere:** **Căutare → Caută în fișiere…** (**Ctrl+Shift+F**) și **Căutare →
  Înlocuiește în fișiere…** (**Ctrl+Shift+H**) — alegeți un **director** și o **mască** de nume de
  fișier, cu aceleași opțiuni de potrivire. Un **Înlocuiește tot** în mai multe fișiere care atinge mai
  mult de un fișier cere mai întâi confirmarea, apoi scrie fișierele închise direct pe disc și poate fi
  **anulat/refăcut** ca o singură operație (butoanele din dialog și din notificare sau **Editare →
  Anulează modificarea din mai multe fișiere**).

## Parcurgerea diagnosticelor

Cu panoul **Diagnostice**, **F8** / **Shift+F8** sar la problema următoare/anterioară — o cale rapidă
de a trece printr-un fișier. Vedeți [Diagnostice și validare](09-diagnostics-and-validation.md).

---

Următorul: [Diagnostice și validare →](09-diagnostics-and-validation.md)