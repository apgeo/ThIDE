# 25. O topografie derivată dintr-un model 3D

> [← Înapoi la Ghidul utilizatorului](README.md) · [Pagina proiectului ThIDE](../../../README.md)

O scanare laser sau un model fotogrammetric al unei peșteri are pereți, dar nu are topografie: nici
stații, nici tronsoane, nici bucle de închis. **Derivă topografie din model 3D** așază o poligonală
Therion într-un asemenea model — un traseu principal, câte galerii laterale cereți, tronsoane apropiate
de lungimea preferată care nu taie niciodată prin rocă, LRUD și radiale — și o scrie ca fișier `.th`
pe care îl puteți compila, desena și continua.

> **O topografie derivată nu este una măsurată.** Fiecare fișier scris începe cu un comentariu care
> spune asta, iar linia `instrument` spune că datele vin dintr-un model. Nu o trimiteți unui cadastru
> speologic drept topografie.

---

## Ce citește

| Format | Observații |
|---|---|
| **STL** (binar sau ASCII), **OBJ**, rețea **PLY** | Fișierul nu are unitate și nici „sus”: ThIDE ghicește (milimetri când modelul e uriaș, altfel metri, Z în sus) și vă spune — verificați. |
| **glb** (glTF binar) | Metri și Y în sus, după regulile formatului. Fișierele comprimate Draco nu sunt citite. |
| Nor de puncte **PLY**, **XYZ**, **PTS** | Doar puncte. Golurile dintre punctele scanate mai înguste de circa 0,4 m sunt închise (`void.holeClosing` într-un fișier de setări); dacă nu se găsește niciun spațiu închis, dați un punct de start în peșteră. |

Modelul trebuie să fie suprafața interioară a peșterii — o rețea închisă sau aproape închisă. Găurile
din scanare nu deranjează; cea mai mare gaură este luată drept intrare.

## Deschiderea

**Instrumente → Derivă topografie din model 3D…**, aceeași comandă din lista *Import* a barei de
instrumente, sau paleta de comenzi.

Fereastra are trei file: **Model**, **Setări** și **Ghid**.

## 1. Modelul

**Răsfoiește…** până la fișier, apoi verificați setările de **Intrare**: **Unități**, **Axa verticală** și
**Rotația nordului** (direcția busolei spre care arată axa +Y a modelului; 0 dacă +Y este nordul). Dacă
modelul este în coordonate reale de hartă, completați **Sistemul de coordonate** (de exemplu `UTM34N`) —
se scrie împreună cu punctul fix. Dacă le schimbați după aceea, apăsați din nou **Deschide**.

**Cum arată modelul** arată apoi dimensiunea în metri, dacă unitatea a fost ghicită, câte găuri are, câte
bucle are (când e închis) și câți voxeli și câtă memorie va cere pasul următor. Dacă dimensiunea pare
greșită, unitatea e greșită.

## 2. Setările

Porniți de la o **Presetare** și schimbați ce vă trebuie:

| Presetare | Pentru |
|---|---|
| **Schelet din scanare** (implicit) | Stații pe axa galeriilor, un inel de radiale, galerii laterale până când se văd 90 % din pereți. |
| **Doar traseul principal** | O singură linie de la start până la capătul cel mai îndepărtat. |
| **Traseul principal și galeriile mari** | Traseul principal și galeriile care pleacă din el, dacă au cel puțin 10 m lungime și 1 m lățime. |
| **Amănunțit** | Galerii pe orice nivel, nișe, 98 % din pereți, radiale adaptive. |
| **Topografie sintetică** | Ce ar fi adus o echipă: stații pe podea, un inel de radiale ca la DistoX, eroare de instrument adăugată. Util pentru teste și pentru învățat. |

**Tronsoane** — **Lungimea preferată**, **Cea mai scurtă** și **Cea mai lungă**, și **Strategia**:

- *Aproape de lungimea preferată*: tronsoane cât de aproape permite galeria;
- *Cât mai puține tronsoane*: vize lungi;
- *Distanțe egale*: stații la distanțe egale;
- *La joncțiuni și coturi*: stații doar la joncțiuni, coturi, strâmtori și intrări în săli.

Oricare ar fi strategia, un tronson nu are voie să treacă mai aproape de perete decât **Distanța față de
pereți**, să iasă din galeria lui sau să taie un colț prin rocă. Unde nimic nu se potrivește, limitele
sunt relaxate treptat, iar rezultatul spune unde. **Cea mai lungă verticală** limitează tronsoanele în
puțuri (gol: un puț poate fi un singur tronson).

**Stații** — **Poziția stației**: pe axa galeriei, pe podea, pe tavan sau pe perete.

**Galerii laterale** — cât de mult se ramifică topografia din traseul principal:

- **Adâncime**: *Doar traseul principal*, *1 nivel* (galeriile din traseul principal), *2 niveluri*
  (și galeriile lor) sau *Nelimitat*;
- **Cea mai scurtă galerie** și **Cea mai îngustă galerie**: cele mai mici nu sunt topografiate;
- **Număr maxim de galerii** și **Buget de lungime**: limite ferme;
- **Bucle**: *Închide buclele*, *Închide buclele dacă e ieftin* (doar când merită, ca orice galerie) sau
  *Nu închide niciodată buclele*;
- **Stații laterale în nișe**: un singur tronson scurt într-o nișă, mai ieftin decât o galerie.

**Acoperire** — **Ținta**: partea din pereți care trebuie să se vadă dintr-o stație. Galeriile se adaugă
una câte una, întâi cea care arată cel mai mult perete nou pe metru de topografie, până la țintă sau până
o oprește o limită. Nu se poate acoperi totul — ar trebui câte o galerie în fiecare fisură — așa că
buzunarele mai puțin adânci decât **Cel mai mic detaliu** nu contează. **Raza** este cât de departe
„vede” o stație (o distanță efectivă a radialelor).

**Radiale** — *Fără*, *LRUD (4 radiale)*, un *Inel* de-a curmezișul galeriei, o *Sferă*, *Adaptiv* (mai
multe radiale unde peretele e neregulat) sau *Uniform pe perete* (capetele răspândite uniform pe perete);
**Radiale pe stație**.

**Gol și săli** — **Dimensiunea voxelului** (0 = automat: un sfert din cea mai îngustă trecere), **Cea mai
îngustă trecere** (legăturile mai înguste sunt tratate ca rocă — fiecare tăietură e raportată), **Cel mai
mic stâlp** (roca mai subțire din galerie, cum ar fi un om sau o coardă scanate, este ignorată) și ce se
face în **Săli mari**: *Nod (o stație centrală)*, *Pe axa lungă*, *Pe perimetru* sau *Păstrează
scheletul*.

**Citiri și rezultat** — **Numele stațiilor** (*Secvențial* 1, 2, 3 … sau *Pe ramuri*: un prefix de literă
pentru fiecare galerie), sub-topografii de o mărime dată, citiri carteziene în loc de ruletă/busolă/
clinometru și **eroare sintetică de instrument** (oprită implicit; fișierul spune când e pornită).

## 3. Ghidul — propriul punct de start și propriile tronsoane

Totul este automat dacă nu spuneți altfel. În fila **Ghid** puteți:

- da un **Punct de start** (devine stația fixă) — debifați *Automat* și scrieți coordonatele;
- adăuga **Puncte** prin care trebuie să treacă traseul principal;
- adăuga **Tronsoane** între puncte — câteva sau pe toate.

De îndată ce cele două puncte ale unui tronson sunt valide, tronsonul este **verificat pe model**: verde
când e liber, galben când trece prea aproape de un perete sau iese din intervalul de lungimi, roșu când
taie prin rocă (cu distanța până la impact și cât de multă rocă traversează) sau când un punct nu este în
galerie. **Înlănțuie punctele în tronsoane** unește startul și punctele, în ordine. **Fixare** mută un
punct pe axa galeriei, pe podea, pe peretele cel mai apropiat sau (pentru un punct ales pe perete) în
galerie.

**Modul** spune ce se întâmplă cu restul: *Completează restul automat* (tronsoanele voastre rămân cum
sunt, iar galeriile pe care le acoperă nu sunt topografiate din nou) sau *Doar tronsoanele indicate*
(doar ale voastre, cu LRUD și radiale). **La tronson blocat**: *Oprește cu eroare*, *Păstrează și
avertizează* sau *Omite*; **Forțează** pe un tronson îl păstrează orice ar spune verificarea (o coardă
scanată poate arăta ca roca).

Punctele pot fi date în *Coordonatele modelului* (metri, Z în sus, nordul aplicat — ce folosește
rezultatul) sau în *Coordonatele fișierului* (cum arată fișierul în alt program) — **Coordonate**.
**Încarcă ghid…** citește un ghid salvat înainte, o poligonală Therion cu un `fix` în coordonatele
modelului sau o topografie `.lox` / `.3d` — ultima vă dă radiale și LRUD din scanare pentru o topografie
pe care o aveți deja.

## 4. Rulare, verificare, salvare

**Rulează** construiește golul și scheletul modelului (partea lentă — de la secunde la minute; este
refolosită cât timp schimbați doar setările de tronsoane, galerii, acoperire și radiale) și așază
topografia. **Rezultat** arată stațiile, tronsoanele și lungimea, galeriile și buclele, lungimile
tronsoanelor, acoperirea atinsă și de ce s-a oprit, radialele și tot ce e blocat, prea aproape de pereți
sau relaxat.

**Previzualizare 3D** arată modelul cu topografia derivată în vizualizatorul 3D. **Salvează .th…** scrie
topografia și, lângă ea, `<nume>.mesh-survey.json` cu toate setările și constatările; *Scrie și
straturile de depanare* adaugă scheletul și zonele pe care nu le vede nicio stație ca fișiere `.ply`,
plus o previzualizare `.lox`.

## Din linia de comandă și dintr-un asistent

`therion-cli mesh-survey inspect|check-leg|run <model>` face același lucru fără interfață; rulat fără
argumente, arată opțiunile. Instrumentele MCP `mesh_survey_inspect`, `mesh_survey_check_leg` și
`mesh_survey_run` îi dau unui asistent același lucru și întorc doar numere (vezi
[configurarea gazdei MCP](../../mcp-host-setup.md)).

## Bine de știut

- Datele reale rămân pe calculatorul vostru: nu se trimite nimic nicăieri.
- Un model în unitatea greșită dă dimensiuni absurde în fila Model; corectați unitatea înainte de rulare.
- Scanările mari cer memorie: fila Model spune cât. Măriți dimensiunea voxelului (sau cea mai îngustă
  trecere) ca să ceară mai puțin.
- Fișierul derivat este un punct de plecare. Redenumiți stațiile, adăugați `team` și date, și păstrați-i
  antetul.
