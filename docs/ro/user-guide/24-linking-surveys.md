# 24. Legarea unui fișier de topografie în proiect

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md) · [Pagina principală a proiectului ThIDE](../../../README.md)

O ieșire nouă sosește de obicei ca fișier `.th` de sine stătător — exportat din TopoDroid, transcris
după ieșire sau trimis de o altă echipă. Ca să devină parte a peșterii, are nevoie de două linii în
proiect: un `input` care aduce fișierul și un `equate` care leagă una dintre stațiile lui de stația
din care continuă. **Leagă un fișier de topografie** le scrie pe amândouă, la locul potrivit și cu
numele de stații corecte.

---

## Deschiderea dialogului

- **Unelte → Leagă un fișier de topografie…** (disponibil și în paleta de comenzi) sau
- clic dreapta într-un editor `.th` și alegeți **Generează cod** ▸ **Leagă un fișier de
  topografie…** — liniile ajung atunci în *acel* fișier, în blocul de topografie din jurul cursorului.

Alegeți fișierul de topografie cu **Răsfoiește…** (sau scrieți calea și apăsați **Enter**). Linia de
sub cale arată câte stații și vize are fișierul și dacă a fost analizat fără erori.

## Alegerea celor două stații

**Stația din topografia nouă** (stânga): filtrați lista tastând sau faceți clic pe o stație în mica
previzualizare de sub ea. Stația aleasă este încercuită în previzualizare. **Deschide într-o
fereastră…** afișează această previzualizare la dimensiune completă; butonul **Previzualizare** o
pliază.

**Stația din proiect** (dreapta): alegeți o **Topografie** pentru a restrânge lista (topografiile sunt
afișate ca arbore indentat, împreună cu fișierul în care se află fiecare), apoi **Stație**.
**Arată în previzualizarea completă…** deschide Previzualizarea principală a întregului proiect, cu
topografia nouă deja atașată la cele două stații — tronsoanele ei desenate cu magenta și ambele stații
încercuite —, ca să puteți verifica, înainte de a scrie ceva, că ajunge unde trebuie. Cât timp este
deschisă, fereastra urmează selecțiile din liste.

## Ce se scrie și unde

Caseta **Linii generate** arată exact ce se va insera, de exemplu:

```
input trips/2026-10-21.th
equate 1@trip_2026_10_21 14@north
```

Numele stațiilor sunt scrise relativ la topografia în care ajung liniile (`point@inner.outer`, așa cum
așteaptă Therion), deci se rezolvă oricât de adânc ar fi imbricate fișierele.

**Inserează în** listează fișierele care pot primi liniile, cele mai potrivite primele:

| Motivul afișat | Semnificație |
|---|---|
| *aici sunt deja echivalări* | blocul de topografie care conține deja echivalările (`equate`) pentru acel nivel |
| *fișier de echivalări* | un fișier cu linii `equate` simple, inclus (`input`) din interiorul acelei topografii |
| *blocul topografiei* | blocul de topografie propriu-zis, încă fără echivalări |
| *la cursor* | locul în care ați dat clic dreapta (intrarea **Generează cod**) |

Urmează, mai jos în listă, topografiile care o cuprind. Topografia nouă devine *soră* cu topografia
căreia îi aparține stația din proiect, așa că `equate` se scrie cu un nivel mai sus. Bifați
**Inserează în fișierul curent** pentru a folosi fișierul deschis în editor, atunci când acesta se
numără printre candidați.

În blocul ales, `input` se adaugă după ultimul `input`, iar `equate` după ultimul `equate` (sau imediat
după `input`, dacă nu există încă niciunul), cu indentarea și terminațiile de linie ale blocului.
**Adaugă un comentariu cu data și fișierul sursă** pune deasupra lor
`# Adăugat pe <dată> din fișierul sursă <nume>`.

## Copierea fișierului în proiect

Un fișier de topografie aflat în afara dosarului proiectului este copiat implicit în proiect (un
proiect nu ar trebui să depindă de fișiere din afara structurii sale); unul aflat deja înăuntru este
inclus de acolo unde se află. Dosarul sugerat este cel în care se află majoritatea celorlalte fișiere
`input` ale blocului — modificați-l sau folosiți **Răsfoiește…**.
Schițele `.th2` din același dosar sunt listate; cele pe care le include chiar fișierul de topografie
sunt bifate și își păstrează poziția relativă față de el, astfel încât propriile linii `input` ale
topografiei rămân valide.

## Verificări înainte de scriere

Dialogul avertizează, cu portocaliu, când fișierul este **deja inclus** undeva, când **există deja o
echivalare cu aceleași stații** sau când o schiță inclusă de topografie **nu se copiază**; puteți
insera oricum. Refuză, cu roșu, când o stație nu poate fi numită din blocul ales (alegeți unul mai
sus), când blocul nu are linie de sfârșit sau când o țintă a copierii există deja cu alt conținut.

## Inserarea

**Inserează** scrie liniile (și copiile) într-un singur pas: în editor, dacă fișierul este deschis —
astfel încât **Anulează** le retrage de acolo —, altfel direct în fișier, păstrându-i
codificarea. Întreaga operație este o singură intrare în [anularea la nivelul spațiului de
lucru](05-interface-tour.md#anularea-unei-unelte-care-a-scris-în-mai-multe-fișiere): **Editare → Anulează modificarea din mai multe fișiere**
(sau butonul **Anulează** din notificare) elimină din nou liniile și fișierele copiate, cu condiția
ca nimic să nu le fi modificat între timp. Proiectul este
apoi recitit, astfel încât Previzualizarea principală și diagnosticele includ imediat topografia nouă.
Un fișier deschis cu modificări nesalvate trebuie salvat mai întâi atunci când i se adaugă un `input`.
**Copiază în clipboard** vă dă liniile fără a modifica nimic.

*Deocamdată dialogul leagă câte o stație de fiecare parte; planificatorul pe care se bazează acceptă
deja echivalări cu mai multe stații, pe care le va oferi o versiune ulterioară.*
