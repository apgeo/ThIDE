# 18. Calculators & converters

> [← Back to the User Guide home](README.md)

Three small utilities under **Tools → Calculators** handle the arithmetic that comes up constantly in
cave surveying. Each produces a result you can **copy** straight into your `.th` files.

## Unit converter

**Tools → Calculators → Unit converter…** converts **length** and **angle** units:

- **Length:** metres, feet, centimetres, kilometres, inches, yards.
- **Angle:** degrees, grads, mils, percent slope, minutes.

Pick a **category**, enter a **value**, choose **From** and **To** units; **swap** the direction and
**copy the result**. Handy when a data sheet is in feet/grads and your project is in metres/degrees.

## Coordinate converter

**Tools → Calculators → Coordinate converter…** converts between **WGS84 lat/long** and **UTM**, both
directions:

- **Lat/long → UTM:** enter latitude, longitude, altitude → get zone, hemisphere, easting, northing.
- **UTM → lat/long:** enter zone, hemisphere, easting, northing → get lat/long.

Best of all, it can **copy a ready-made `fix` line** in either UTM or lat-long form, so georeferencing
a station is copy-paste. Use it together with the coordinate systems your thconfig declares.

## Declination calculator

**Tools → Calculators → Declination calculator…** computes **magnetic declination** for a location and
date using a geomagnetic model (WMM / IGRF spherical-harmonic synthesis).

1. **Load a model:** ThIDE needs a `WMM.COF` (or IGRF `.COF`) file — download one (NOAA, public
   domain) and drop it in `%AppData%/ThIDE`, or use **Load model…** to pick it. Until then the panel
   says *No magnetic model loaded*.
2. Enter the location and **year**; read off the declination (° east/west).
3. **Copy declination line** to paste a `declination` value into your survey.

This is the same engine the [Structural Geology](15-structural-geology.md) module can use to rotate
strike/dip to true north.

---

Next: [Settings & preferences →](19-settings-and-preferences.md)
