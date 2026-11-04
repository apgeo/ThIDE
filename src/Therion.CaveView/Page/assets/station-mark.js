// Station mark for a CaveView.js viewer (the apgeo fork, 2.9.0-slx.22 or later): flies the
// camera to a station - centred only, or with a close-up - and draws a crosshair over it that
// follows the station as the model is turned, until it is cleared. A splay, which has no name,
// is pointed at through the station it was shot from and its measured direction.
//
// Shared by the ThIDE 3D viewer and the stand-alone model page. Plain script, no build step:
// it defines window.CaveStationMark.
(function () {
    "use strict";

    // the width of survey, in metres, a close-up takes in about the station
    var CLOSE_UP_SPAN = 30;

    // a splay is taken as found when its direction is within this many degrees of the one
    // measured: enough for declination and grid convergence, which the measurement leaves out
    var SPLAY_MATCH_DEGREES = 25;

    var SVG_NS = "http://www.w3.org/2000/svg";

    function el(name, attrs) {
        var e = document.createElementNS(SVG_NS, name);
        for (var k in attrs) e.setAttribute(k, attrs[k]);
        return e;
    }

    // the crosshair: a ring with four ticks, each drawn twice - a dark halo under a bright line -
    // so it reads over the black background, over bright walls and over the white of a print
    function buildCrosshair() {
        var svg = el("svg", { width: 72, height: 72, viewBox: "-36 -36 72 72", "class": "cv-mark-cross" });
        var shapes = [
            ["circle", { cx: 0, cy: 0, r: 15 }],
            ["line", { x1: 0, y1: -34, x2: 0, y2: -19 }],
            ["line", { x1: 0, y1: 19, x2: 0, y2: 34 }],
            ["line", { x1: -34, y1: 0, x2: -19, y2: 0 }],
            ["line", { x1: 19, y1: 0, x2: 34, y2: 0 }]
        ];
        ["cv-mark-halo", "cv-mark-line"].forEach(function (cls) {
            var g = el("g", { "class": cls });
            shapes.forEach(function (s) { g.appendChild(el(s[0], s[1])); });
            svg.appendChild(g);
        });
        return svg;
    }

    var STYLE =
        ".cv-mark { position: absolute; left: 0; top: 0; width: 0; height: 0; pointer-events: none; z-index: 20; }" +
        ".cv-mark[hidden] { display: none; }" +
        ".cv-mark-cross { position: absolute; left: -36px; top: -36px; overflow: visible; }" +
        ".cv-mark-halo { fill: none; stroke: rgba(0,0,0,0.75); stroke-width: 5; stroke-linecap: round; }" +
        ".cv-mark-line { fill: none; stroke: #ff3b6b; stroke-width: 2.5; stroke-linecap: round; }" +
        ".cv-mark-label { position: absolute; left: 22px; top: 18px; white-space: nowrap; padding: 1px 6px;" +
        "  font: 12px/1.5 system-ui, 'Segoe UI', sans-serif; color: #fff; background: rgba(0,0,0,0.72);" +
        "  border-left: 3px solid #ff3b6b; border-radius: 2px; }" +
        ".cv-mark.cv-mark-arrive .cv-mark-cross { animation: cv-mark-arrive 700ms ease-out 1; }" +
        "@keyframes cv-mark-arrive { from { transform: scale(2.6); opacity: 0; } to { transform: scale(1); opacity: 1; } }" +
        "@media (prefers-reduced-motion: reduce) { .cv-mark.cv-mark-arrive .cv-mark-cross { animation: none; } }";

    function ensureStyle() {
        if (document.getElementById("cv-mark-style")) return;
        var s = document.createElement("style");
        s.id = "cv-mark-style";
        s.textContent = STYLE;
        document.head.appendChild(s);
    }

    // the measured direction of a shot as a vector east, north and up (degrees, any length unit)
    function measuredVector(shot) {
        var az = (shot.compass || 0) * Math.PI / 180;
        var cl = (shot.clino || 0) * Math.PI / 180;
        var len = shot.length || 1;
        return { x: len * Math.cos(cl) * Math.sin(az), y: len * Math.cos(cl) * Math.cos(az), z: len * Math.sin(cl) };
    }

    function angleBetween(a, b) {
        var la = Math.hypot(a.x, a.y, a.z), lb = Math.hypot(b.x, b.y, b.z);
        if (la === 0 || lb === 0) return 180;
        var c = (a.x * b.x + a.y * b.y + a.z * b.z) / (la * lb);
        return Math.acos(Math.max(-1, Math.min(1, c))) * 180 / Math.PI;
    }

    // The splay of a station that best matches a measured shot: the nearest in direction, the
    // nearer in length between two of about the same direction. Null when the model has none there.
    function pickSplay(splays, shot) {
        if (!splays || splays.length === 0) return null;
        var want = measuredVector(shot);
        var wantLength = Math.hypot(want.x, want.y, want.z);
        var best = null;
        splays.forEach(function (s) {
            var angle = angleBetween(s, want);
            var length = Math.hypot(s.x, s.y, s.z);
            var score = angle + 10 * Math.abs(Math.log((length || 1e-6) / (wantLength || 1e-6)));
            if (best === null || score < best.score) best = { offset: s, angle: angle, score: score };
        });
        return best;
    }

    // overlay: an element laid exactly over the viewer's container (position: absolute, inset 0
    // inside a common positioned parent); the mark is drawn in it
    function CaveStationMark(viewer, overlay) {
        ensureStyle();
        this.viewer = viewer;
        this.ref = null;
        this.offset = null;
        this.node = document.createElement("div");
        this.node.className = "cv-mark";
        this.node.hidden = true;
        this.node.appendChild(buildCrosshair());
        this.label = document.createElement("div");
        this.label.className = "cv-mark-label";
        this.node.appendChild(this.label);
        overlay.appendChild(this.node);
        this._frame = 0;
        this._tick = this._tick.bind(this);
        this._index = null;
        var self = this;
        viewer.addEventListener("newCave", function () { self._index = null; self.clear(); });
    }

    CaveStationMark.CLOSE_UP_SPAN = CLOSE_UP_SPAN;

    // The model's name for a station named by a source that may see its surveys from another
    // level: the name itself when the model has it, else the one station whose path ends with
    // it, or that it ends with (a model compiled from a parent survey adds a level; one compiled
    // from inside it drops one). Returned as an array of names, which CaveView matches exactly.
    // The name unchanged when no station, or more than one, fits.
    CaveStationMark.prototype.resolve = function (ref) {
        var viewer = this.viewer;
        if (Array.isArray(ref) || viewer.getStationSplays(ref) !== null) return ref;
        var name = String(ref);
        if (this._index === null) {
            var index = Object.create(null);
            var walk = function (node, names) {
                if (node.isStation()) {
                    var full = names.join(".");
                    var last = full.slice(full.lastIndexOf(".") + 1);
                    (index[last] || (index[last] = [])).push({ full: full, names: names });
                } else {
                    node.children.forEach(function (c) { walk(c, names.concat([c.name])); });
                }
            };
            try { walk(viewer.getSurveyTree(), []); } catch (e) { /* no model yet */ }
            this._index = index;
        }
        var last = name.slice(name.lastIndexOf(".") + 1);
        var fits = (this._index[last] || []).filter(function (c) {
            return c.full.endsWith("." + name) || name.endsWith("." + c.full);
        });
        return fits.length === 1 ? fits[0].names : ref;
    };

    // Draw the mark on a station (or on the point offset from it) without moving the camera.
    CaveStationMark.prototype.mark = function (ref, options) {
        options = options || {};
        this.ref = ref;
        this.offset = options.offset || null;
        this.label.textContent = options.label === undefined
            ? (Array.isArray(ref) ? ref.join(".") : String(ref))
            : (options.label || "");
        this.label.hidden = !this.label.textContent;
        this.node.classList.remove("cv-mark-arrive");
        void this.node.offsetWidth;   // restart the arrival animation
        this.node.classList.add("cv-mark-arrive");
        if (!this._frame) this._frame = requestAnimationFrame(this._tick);
    };

    CaveStationMark.prototype.clear = function () {
        this.ref = null;
        this.offset = null;
        this.node.hidden = true;
        if (this._frame) cancelAnimationFrame(this._frame);
        this._frame = 0;
    };

    CaveStationMark.prototype._tick = function () {
        this._frame = 0;
        if (this.ref === null) return;
        var at = null;
        try { at = this.viewer.getStationScreenPosition(this.ref, this.offset || undefined); } catch (e) { at = null; }
        if (at) {
            this.node.style.transform = "translate(" + at.x + "px," + at.y + "px)";
            this.node.hidden = false;
        } else {
            this.node.hidden = true;   // behind the camera or off the view: it goes with the station
        }
        this._frame = requestAnimationFrame(this._tick);
    };

    // Fly to a station and mark it.
    //   options.zoom   true for a close-up, false to centre it at the current zoom
    //   options.span   width of the close-up in metres (default CLOSE_UP_SPAN)
    //   options.splay  { length, compass, clino } of a splay shot from the station: the mark
    //                  goes on the end of the splay of the model that best matches it
    //   options.label  text beside the mark (default: the station's name)
    // Resolves with { kind: "station" | "survey" | "none" | "cancelled", splay: null |
    // "matched" | "closest" | "missing", angle }. A name that is a survey section rather than a
    // station is framed instead, with no mark; "cancelled" is a flight another move replaced.
    CaveStationMark.prototype.show = function (ref, options) {
        options = options || {};
        var viewer = this.viewer;
        ref = this.resolve(ref);

        // getStationSplays() answers null exactly when the name is not a station of the model
        var splays = viewer.getStationSplays(ref);
        if (splays === null) {
            this.clear();
            if (typeof viewer.focusSurvey !== "function") return Promise.resolve({ kind: "none" });
            return viewer.focusSurvey(ref).then(
                function () { return { kind: "survey" }; },
                function () { return { kind: "none" }; });
        }

        var result = { kind: "station", splay: null, angle: null, name: Array.isArray(ref) ? ref.join(".") : String(ref) };
        var offset = null;
        if (options.splay) {
            var best = pickSplay(splays, options.splay);
            if (best === null) {
                result.splay = "missing";
            } else {
                offset = best.offset;
                result.angle = Math.round(best.angle);
                result.splay = best.angle <= SPLAY_MATCH_DEGREES ? "matched" : "closest";
            }
        }

        // marked at once, so the crosshair rides with the station while the camera flies
        this.mark(ref, { offset: offset, label: options.label });

        // the camera centres on the station a splay was shot from; a close-up wide enough
        // to take in the splay's end as well
        var span = options.span || CLOSE_UP_SPAN;
        if (offset) span = Math.max(span, 3 * Math.hypot(offset.x, offset.y, offset.z));

        var focus = options.zoom
            ? { span: span, popup: false }
            : { keepView: true, popup: false };

        return viewer.focusStation(ref, focus).then(
            function () { return result; },
            function () { return { kind: "cancelled" }; });
    };

    CaveStationMark.pickSplay = pickSplay;
    window.CaveStationMark = CaveStationMark;
})();
