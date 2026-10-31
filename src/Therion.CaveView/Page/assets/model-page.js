// Stand-alone page for a cave survey model: a CaveView.js viewer filling the page, its toolbar,
// a list of places that fly the camera to a station, and links (#station=<name>) that open the
// page on one. The page is configured by the JSON block #cave-page-config written into index.html;
// nothing here is specific to the cave shown. Needs station-mark.js and the CaveView bundle.
(function () {
    "use strict";

    var TEXT = {
        en: {
            loading: "Loading the 3D model…",
            noWebGL: "This browser cannot draw 3D graphics (WebGL is not available), so the model cannot be shown.",
            noEngine: "The 3D viewer could not be loaded.",
            fromDisk: "The model could not be loaded. A page opened straight from a disk cannot read the model file next to it: put the folder on a web server, or open it through one (for example: python3 -m http.server).",
            places: "Places",
            notFound: "“{0}” is not a station or survey of this model.",
            closeUp: "Close-up",
            closeUpTip: "Zoom in on a place when going to it"
        },
        ro: {
            loading: "Se încarcă modelul 3D…",
            noWebGL: "Acest browser nu poate desena grafică 3D (WebGL nu este disponibil), așa că modelul nu poate fi afișat.",
            noEngine: "Vizualizatorul 3D nu a putut fi încărcat.",
            fromDisk: "Modelul nu a putut fi încărcat. O pagină deschisă direct de pe disc nu poate citi fișierul modelului de lângă ea: puneți dosarul pe un server web sau deschideți-l printr-unul (de exemplu: python3 -m http.server).",
            places: "Locuri",
            notFound: "„{0}” nu este o stație sau o topografie a acestui model.",
            closeUp: "Apropiere",
            closeUpTip: "Apropie imaginea de un loc când mergi la el"
        }
    };

    var config = JSON.parse(document.getElementById("cave-page-config").textContent);
    var text = TEXT[config.language] || TEXT.en;
    var status = document.getElementById("cp-status");

    function say(message) {
        status.textContent = message || "";
        status.hidden = !message;
    }

    if (typeof CV2 === "undefined") { say(text.noEngine); return; }
    if (typeof Detector !== "undefined" && !Detector.webgl) { say(text.noWebGL); return; }

    var view = config.view || {};
    var shading = {
        height: CV2.SHADING_HEIGHT, survey: CV2.SHADING_SURVEY, length: CV2.SHADING_LENGTH,
        inclination: CV2.SHADING_INCLINATION, single: CV2.SHADING_SINGLE
    };
    var orientation = {
        plan: CV2.VIEW_PLAN, north: CV2.VIEW_ELEVATION_N, south: CV2.VIEW_ELEVATION_S,
        east: CV2.VIEW_ELEVATION_E, west: CV2.VIEW_ELEVATION_W
    };

    var viewer = new CV2.CaveViewer("cp-model", {
        home: config.home,
        surveyDirectory: config.surveyDirectory || "",
        language: config.language || "en",
        fullscreenElement: "cp-stage",
        view: { shadingMode: shading[view.shading] !== undefined ? shading[view.shading] : CV2.SHADING_HEIGHT }
    });

    if (config.toolbar !== false && typeof CV2.CaveViewToolbar === "function") {
        new CV2.CaveViewToolbar(viewer, "cp-model", {
            placement: "top",
            buttons: ["viewPlan", "viewNorth", "viewEast", "viewSouth", "viewWest", "cameraPerspective",
                "cameraOrthographic", "shadingMode", "walls", "splays", "stations", "stationLabels",
                "entrances", "boundingBox", "autoRotate", "fullscreen"]
        });
    }

    var mark = new CaveStationMark(viewer, document.getElementById("cp-overlay"));
    var closeUp = config.closeUp !== false;
    var loaded = false;
    var pending = null;   // a station asked for (by the address) before the model arrived

    function go(ref, label) {
        if (!loaded) { pending = { ref: ref, label: label }; return; }
        mark.show(ref, { zoom: closeUp, span: config.closeUpSpan, label: label }).then(function (r) {
            say(r.kind === "none" ? text.notFound.replace("{0}", ref) : "");
        });
    }

    // #station=<name> opens the page on a station; a place in the list writes it, so a link
    // copied from the address bar shows somebody else the same place
    function fromAddress() {
        var m = /(?:^#|&)station=([^&]+)/.exec(location.hash);
        if (m) go(decodeURIComponent(m[1]));
    }
    window.addEventListener("hashchange", fromAddress);

    function buildPlaces() {
        var places = config.places || [];
        var aside = document.getElementById("cp-places");
        if (places.length === 0) return;
        aside.querySelector("h2").textContent = text.places;
        var list = aside.querySelector("ul");
        places.forEach(function (place) {
            var li = document.createElement("li");
            var a = document.createElement("a");
            a.href = "#station=" + encodeURIComponent(place.station);
            a.textContent = place.label || place.station;
            a.addEventListener("click", function (e) {
                e.preventDefault();
                history.replaceState(null, "", a.getAttribute("href"));
                go(place.ref || place.station, place.label || place.station);
            });
            li.appendChild(a);
            if (place.note) {
                var note = document.createElement("span");
                note.className = "cp-note";
                note.textContent = place.note;
                li.appendChild(note);
            }
            list.appendChild(li);
        });

        var toggle = document.createElement("label");
        toggle.className = "cp-closeup";
        toggle.title = text.closeUpTip;
        var box = document.createElement("input");
        box.type = "checkbox";
        box.checked = closeUp;
        box.addEventListener("change", function () { closeUp = box.checked; });
        toggle.appendChild(box);
        toggle.appendChild(document.createTextNode(" " + text.closeUp));
        aside.insertBefore(toggle, list);

        aside.hidden = false;
    }

    // other links of the page can fly the model too: <a data-station="cave.12">…</a>
    document.addEventListener("click", function (e) {
        var a = e.target.closest && e.target.closest("a[data-station]");
        if (!a) return;
        e.preventDefault();
        go(a.getAttribute("data-station"), a.textContent);
    });

    // a station picked in the model is marked where it is, without moving the camera
    viewer.addEventListener("station", function (e) {
        var n = e && e.node;
        var name = n && (typeof n.name === "function" ? n.name() : n.name);
        if (name) mark.mark(name);
    });

    viewer.addEventListener("newCave", function () {
        loaded = true;
        say("");
        try {
            viewer.walls = view.walls !== false;
            viewer.splays = !!view.splays;
            viewer.stations = !!view.stations;
            viewer.stationLabels = !!view.stationLabels;
            viewer.entrances = view.entrances !== false;
            viewer.box = !!view.box;
            viewer.cameraType = view.orthographic ? CV2.CAMERA_ORTHOGRAPHIC : CV2.CAMERA_PERSPECTIVE;
            if (orientation[view.orientation] !== undefined) viewer.view = orientation[view.orientation];
            if (view.autoRotate) viewer.autoRotate = true;
        } catch (e) { /* a setting the bundle does not know is left as it is */ }
        if (pending) { var p = pending; pending = null; go(p.ref, p.label); }
    });

    // CaveView re-fits its canvas on a window resize only; the stage also changes size when the
    // places panel folds on a narrow screen
    if (typeof ResizeObserver === "function") {
        new ResizeObserver(function () { window.dispatchEvent(new Event("resize")); })
            .observe(document.getElementById("cp-model"));
    }

    // for a page author's own script: the viewer, and going to a station as the list does
    window.CavePage = { viewer: viewer, show: go, mark: mark };

    buildPlaces();
    say(text.loading);

    // A model carried in model/model.js (written for a page opened straight from a disk, where
    // the browser refuses to fetch a file next to the page) is handed over as a File.
    var embedded = window.CAVE_PAGE_MODEL;
    if (embedded && embedded.base64) {
        var raw = atob(embedded.base64);
        var bytes = new Uint8Array(raw.length);
        for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
        viewer.loadCave(new File([bytes], embedded.name));
    } else {
        if (location.protocol === "file:") {
            window.addEventListener("error", function () { if (!loaded) say(text.fromDisk); });
            setTimeout(function () { if (!loaded) say(text.fromDisk); }, 4000);
        }
        viewer.loadCave(config.model);
    }

    fromAddress();
})();
