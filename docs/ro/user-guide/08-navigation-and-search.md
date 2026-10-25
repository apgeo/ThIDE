# 8. Navigare și căutare

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Pentru că ThIDE înțelege întregul proiect ([graful de obiecte](04-core-concepts.md#graful-de-obiecte-modelul-semantic)),
vă poate purta prin el după *sens*, nu doar după text. Aici se câștigă mult timp.

## Paleta de comenzi

**Ctrl+Shift+P** deschide **Paleta de comenzi** — o singură casetă de căutare pentru orice. Începeți să tastați
numele unei comenzi (**Compilează**, **Comută Diagnosticele**, **Formatează documentul**, **Comută la română**…). Comenzile sunt grupate pe
categorii (**Fișier**, **Compilare**, **Vizualizare**, **Navigare**, **Căutare**, **Editare**, **Editor**, **Setări**, **Documentație**, …).

Paleta servește și drept casetă de salt, prin **prefixe**:

| Tastați | Efect |
|---|---|
| *(text)* | Potrivire aproximativă a unei comenzi de executat |
| `@name` | Salt la un simbol **din documentul curent** |
| `#name` | Salt la un simbol **din întregul spațiu de lucru** (survey-uri, stații, scrap-uri, hărți) |
| `:42` | Salt la **linia 42** a fișierului curent |

## Deschiderea fișierelor și comutarea între ele

| Acțiune | Scurtătură | Observații |
|---|---|---|
| **Salt la fișier** | **Ctrl+P** | Deschidere de fișier cu potrivire aproximativă. Domeniul se poate configura: doar fișierele conectate la thconfig-ul activ sau toate fișierele Therion din spațiul de lucru ([Setări → Proiect](19-settings-and-preferences.md#proiect)). |
| Comutare între documentele deschise | **Ctrl+Tab** | Parcurge filele deschise. |
| **Redeschide fila închisă** | — | Meniul **Fișier**; readuce ultimul document închis. |
| Afișarea fișierului activ în arbore | — | **Paletă de comenzi** → *Afișează fișierul activ în spațiul de lucru*. |

## Salt la simbol

- **Salt la simbol în document** și **Salt la simbol în spațiul de lucru** (butoane din bara de instrumente, **Paletă de comenzi** sau
  prefixele `@` / `#`). Caută în survey-uri, stații, scrap-uri și hărți.
- Peșteri foarte mari: numărul de stații listate este limitat, pentru ca aplicația să rămână receptivă
  ([Setări → Performanță](19-settings-and-preferences.md#performanță)).

## Salt după sens

De pe un identificator din editor (cardul de hover sau clic dreapta):

| Acțiune | Scurtătură | Ce face |
|---|---|---|
| **Salt la definiție** | clic / Ctrl+clic / dublu-clic* | Sare la locul unde este declarată o stație, un survey sau un fișier. |
| **Salt la equate** | — | Urmează un `equate` până la stația îmbinată. |
| **Salt la harta care îl agregă** | — | De la un scrap sau o hartă, sare la harta care îl include. |
| **Găsește toate referințele** | — | Listează fiecare utilizare din tot proiectul. |
| **Vezi definiția** | **Alt+F12** | Arată definiția direct în text, fără să părăsiți locul în care vă aflați. |
| **Salt la blocul pereche** | **Ctrl+]** | Între deschiderea unui bloc și `end…` corespunzător. |
| **Intră în fișierul inclus** | **Alt+↓** | Deschide fișierul indicat de o linie `input`. |

\* Dacă un singur clic navighează sau doar plasează cursorul este o preferință
(**Necesită dublu-clic pentru a merge la definiție**) — Ctrl+clic navighează întotdeauna. Consultați
[Setări → Editor](19-settings-and-preferences.md#editor).

## Istoricul cursorului: Înapoi și Înainte

**Alt+Left** / **Alt+Right** (și săgețile din bara de instrumente) parcurg istoricul cursorului și al navigării — săriți la o
definiție, apoi reveniți imediat unde erați, chiar și între fișiere.

## Redenumirea unui simbol peste tot

**F2** (sau *Redenumește simbolul* din cardul de hover / paletă) redenumește o stație sau un survey **în fiecare fișier
care îl referă**. Dacă este activată, o **previzualizare** listează mai întâi toate aparițiile (numărul lor în N fișiere),
pe care le puteți inspecta cu dublu-clic înainte de aplicare. Activați sau dezactivați previzualizarea din
[Setări → Editor](19-settings-and-preferences.md#editor). Stațiile pot fi redenumite și din
grila **Măsurători** (clic dreapta → *Redenumește stația…*).

## Căutare și înlocuire

- **În fișierul curent:** **Caută** și **Înlocuiește** (meniul **Căutare**) — cu opțiuni pentru potrivirea majusculelor, cuvânt întreg și
  expresii regulate.
- **În mai multe fișiere:** **Caută în fișiere** (**Ctrl+Shift+F**) și **Înlocuiește în fișiere** — alegeți un
  **director** și o **mască** de nume de fișier, cu aceleași opțiuni de potrivire. O operație **Înlocuiește tot** în mai multe fișiere
  poate fi **anulată/refăcută** ca o singură operație (butoane în dialog).

## Parcurgerea diagnosticelor

Cu panoul **Diagnostice**, **F8** / **Shift+F8** sar la problema următoare/anterioară — o modalitate rapidă
de a trece prin tot fișierul. Consultați [Diagnostice și validare](09-diagnostics-and-validation.md).

---

Următorul: [Diagnostice și validare →](09-diagnostics-and-validation.md)
