# 18. Calculatoare și convertoare

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Trei utilitare mici din **Unelte → Calculatoare** se ocupă de calculele care apar mereu în topografia
speologică. Fiecare produce un rezultat pe care îl puteți **copia** direct în fișierele `.th`.

## Convertor de unități

**Unelte → Calculatoare → Convertor de unități…** convertește unități de **lungime** și de **unghi**:

- **Lungime:** metri, picioare, centimetri, kilometri, țoli, iarzi.
- **Unghi:** grade, grade centesimale, miimi, pantă procentuală, minute.

Alegeți o **Categorie**, introduceți o **Valoare**, alegeți unitățile **Din** și **În**; **inversați**
sensul conversiei și **copiați rezultatul**. Util când o fișă de date este în picioare/grade
centesimale, iar proiectul în metri/grade.

## Convertor de coordonate

**Unelte → Calculatoare → Convertor de coordonate…** convertește între **lat/long WGS84** și **UTM**,
în ambele sensuri:

- *Din lat/long în UTM:* introduceți latitudinea, longitudinea și altitudinea și citiți zona,
  emisfera, coordonata est (easting) și coordonata nord (northing).
- *Din UTM în lat/long:* introduceți zona, emisfera, coordonata est și coordonata nord și citiți
  lat/long.

Cel mai util, poate **copia o linie `fix` gata făcută**, fie în formă UTM, fie lat-long, astfel încât
georeferențierea unei stații devine un simplu copiat-lipit. Folosiți-l împreună cu sistemele de
coordonate declarate în thconfig.

## Calculator de declinație

**Unelte → Calculatoare → Calculator de declinație…** calculează **declinația magnetică** pentru un loc
și o dată folosind un model geomagnetic (sinteză în armonici sferice WMM / IGRF).

1. **Încărcați un model:** ThIDE are nevoie de un fișier `WMM.COF` (sau un `.COF` IGRF) — descărcați
   unul (NOAA, domeniu public) și puneți-l în dosarul de setări al ThIDE (panoul indică exact calea;
   vedeți [Instalare](02-installation-and-setup.md#6-unde-își-stochează-thide-setările)) sau folosiți **Încarcă model…** pentru a-l alege. Până atunci, panoul
   afișează *Niciun model magnetic încărcat*.
2. Introduceți locul și **anul**; citiți declinația (° est/vest).
3. **Copiază linia de declinație** pentru a lipi o valoare `declination` în topografie.

Este același motor pe care îl poate folosi modulul [Geologie structurală](15-structural-geology.md)
pentru a roti direcția/înclinarea spre nordul adevărat.

---

Următorul: [Setări și preferințe →](19-settings-and-preferences.md)
