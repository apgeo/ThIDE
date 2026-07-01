"use strict";
// STRUCT-01 Phase 4 — structural-geology 3D plot (three.js). Renders fitted planes as oriented
// translucent discs plus the cave main-line, in an E/N/Up (Z-up) scene with a compact custom orbit
// control (no OrbitControls dependency).
//
//   C# -> JS : InvokeScript("stRender(<json>)") / "stClear()" / "stResize()" / "stFit()"
//   JS -> C# : window.chrome.webview.postMessage(json)  — {type:"ready"} once, {type:"pick",name}
//
// Every entry point is defensive: a JS throw must never blank the panel.

(function () {
    function post(obj) {
        try {
            var json = JSON.stringify(obj);
            if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage)
                window.chrome.webview.postMessage(json);
            else if (window.parent) window.parent.postMessage(json, "*");
        } catch (e) { /* never throw from the bridge */ }
    }
    // Forward console + uncaught errors to the C# Log panel.
    (function () {
        var c = window.console || (window.console = {});
        ["log", "info", "warn", "error"].forEach(function (m) {
            var orig = (typeof c[m] === "function") ? c[m].bind(c) : function () { };
            c[m] = function () {
                try {
                    var parts = [];
                    for (var i = 0; i < arguments.length; i++) {
                        var a = arguments[i];
                        parts.push(typeof a === "string" ? a : (function () { try { return JSON.stringify(a); } catch (e) { return String(a); } })());
                    }
                    post({ type: "console", level: m, message: parts.join(" ") });
                } catch (e) { }
                orig.apply(c, arguments);
            };
        });
        window.addEventListener("error", function (e) { post({ type: "console", level: "error", message: "" + (e && e.message) }); });
    })();

    var PALETTE = ["#4FC3F7", "#FF8A65", "#AED581", "#BA68C8", "#FFD54F", "#4DB6AC", "#F06292", "#9575CD",
                   "#DCE775", "#64B5F6", "#FFB74D", "#81C784"];

    var renderer, scene, camera, raycaster;
    var planeGroup, lineGroup, pickables = [];
    var gizmoScene, gizmoCam;     // corner compass/vertical reference (N/E/S/W + Up), synced to the view
    var target = new THREE.Vector3(0, 0, 0);
    var sph = { r: 50, theta: 0.9, phi: 1.0 };  // azimuth around Z, polar from +Z

    var bgColor = 0x1e1e1e;

    function init() {
        var host = document.getElementById("scene");
        // preserveDrawingBuffer so toDataURL() can capture the canvas for image export.
        renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
        renderer.setPixelRatio(window.devicePixelRatio || 1);
        renderer.setSize(host.clientWidth, host.clientHeight);
        renderer.autoClear = false;   // we clear manually so the gizmo can overlay in a corner
        host.appendChild(renderer.domElement);

        scene = new THREE.Scene();
        scene.background = new THREE.Color(bgColor);

        camera = new THREE.PerspectiveCamera(55, aspect(), 0.01, 1e7);
        camera.up.set(0, 0, 1);   // Z is up (E/N/Up frame)

        scene.add(new THREE.AmbientLight(0xffffff, 0.85));
        var dir = new THREE.DirectionalLight(0xffffff, 0.5);
        dir.position.set(1, 1, 2);
        scene.add(dir);

        planeGroup = new THREE.Group();
        lineGroup = new THREE.Group();
        scene.add(lineGroup);
        scene.add(planeGroup);

        raycaster = new THREE.Raycaster();
        bindControls(host);
        try { initGizmo(); } catch (e) { gizmoScene = null; post({ type: "console", level: "error", message: "gizmo init failed: " + e }); }
        applyCamera();
        animate();
        window.addEventListener("resize", resize);
        post({ type: "ready" });
    }

    // ---- corner reference gizmo (compass N/E/S/W + vertical Up axis) ---------------------------
    function initGizmo() {
        gizmoScene = new THREE.Scene();
        gizmoCam = new THREE.PerspectiveCamera(40, 1, 0.1, 100);
        gizmoCam.up.set(0, 0, 1);

        var g = new THREE.Group();
        addGizmoAxis(g, new THREE.Vector3(1, 0, 0), "#ff6b6b", "E");
        addGizmoAxis(g, new THREE.Vector3(-1, 0, 0), "#c0392b", "W");
        addGizmoAxis(g, new THREE.Vector3(0, 1, 0), "#51cf66", "N");
        addGizmoAxis(g, new THREE.Vector3(0, -1, 0), "#2f9e44", "S");
        addGizmoAxis(g, new THREE.Vector3(0, 0, 1), "#74c0fc", "Up");
        // Faint horizon ring in the E/N plane so it reads as a compass rose.
        g.add(new THREE.Mesh(
            new THREE.RingGeometry(0.94, 1.0, 48),
            new THREE.MeshBasicMaterial({ color: 0x999999, side: THREE.DoubleSide, transparent: true, opacity: 0.5 })));
        gizmoScene.add(g);
    }

    function addGizmoAxis(g, dir, css, label) {
        var geo = new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(0, 0, 0), dir.clone()]);
        g.add(new THREE.Line(geo, new THREE.LineBasicMaterial({ color: new THREE.Color(css) })));
        var sp = makeLabel(label, css);
        sp.position.copy(dir.clone().multiplyScalar(1.32));
        g.add(sp);
    }

    function makeLabel(text, css) {
        var s = 64, cnv = document.createElement("canvas");
        cnv.width = cnv.height = s;
        var ctx = cnv.getContext("2d");
        ctx.clearRect(0, 0, s, s);
        ctx.fillStyle = css;
        ctx.font = "bold 40px sans-serif";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText(text, s / 2, s / 2 + 2);
        var tex = new THREE.CanvasTexture(cnv);
        tex.needsUpdate = true;
        var sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, transparent: true, depthTest: false, depthWrite: false }));
        sp.scale.set(0.5, 0.5, 0.5);
        return sp;
    }

    // Renders the gizmo into a small bottom-right viewport, matching the main camera's orientation.
    function renderGizmo() {
        if (!gizmoScene) return;
        try { renderGizmoCore(); }
        catch (e) { gizmoScene = null; post({ type: "console", level: "error", message: "gizmo render failed: " + e }); }
    }

    function renderGizmoCore() {
        var host = document.getElementById("scene");
        var W = host.clientWidth, H = host.clientHeight;
        var size = Math.max(64, Math.min(120, Math.round(Math.min(W, H) * 0.22))), m = 8;

        var dir = new THREE.Vector3().subVectors(camera.position, target);
        if (dir.lengthSq() < 1e-9) dir.set(0, -1, 0.4);
        dir.normalize();
        gizmoCam.position.copy(dir.multiplyScalar(3.4));
        gizmoCam.up.copy(camera.up);
        gizmoCam.lookAt(0, 0, 0);
        gizmoCam.updateProjectionMatrix();

        renderer.clearDepth();
        renderer.setScissorTest(true);
        renderer.setScissor(W - size - m, m, size, size);
        renderer.setViewport(W - size - m, m, size, size);
        renderer.render(gizmoScene, gizmoCam);
        renderer.setScissorTest(false);
    }

    function aspect() {
        var host = document.getElementById("scene");
        return Math.max(1e-3, host.clientWidth / Math.max(1, host.clientHeight));
    }

    function applyCamera() {
        var sp = Math.sin(sph.phi), cp = Math.cos(sph.phi), st = Math.sin(sph.theta), ct = Math.cos(sph.theta);
        camera.position.set(
            target.x + sph.r * sp * ct,
            target.y + sph.r * sp * st,
            target.z + sph.r * cp);
        camera.lookAt(target);
    }

    function animate() {
        requestAnimationFrame(animate);
        var host = document.getElementById("scene");
        renderer.setScissorTest(false);
        renderer.setViewport(0, 0, host.clientWidth, host.clientHeight);
        renderer.clear();                 // autoClear is off — clear the full frame ourselves
        renderer.render(scene, camera);
        renderGizmo();
    }

    function resize() {
        var host = document.getElementById("scene");
        renderer.setSize(host.clientWidth, host.clientHeight);
        camera.aspect = aspect();
        camera.updateProjectionMatrix();
    }
    window.stResize = resize;

    // ---- controls (rotate / zoom / pan) -------------------------------------------------------
    function bindControls(host) {
        var dragging = 0, lx = 0, ly = 0;
        host.addEventListener("contextmenu", function (e) { e.preventDefault(); });
        host.addEventListener("mousedown", function (e) { dragging = (e.button === 2 ? 2 : 1); lx = e.clientX; ly = e.clientY; });
        window.addEventListener("mouseup", function () { dragging = 0; });
        window.addEventListener("mousemove", function (e) {
            if (!dragging) return;
            var dx = e.clientX - lx, dy = e.clientY - ly; lx = e.clientX; ly = e.clientY;
            if (dragging === 1) {
                sph.theta -= dx * 0.006;
                sph.phi = Math.max(0.05, Math.min(Math.PI - 0.05, sph.phi - dy * 0.006));
            } else {
                pan(dx, dy);
            }
            applyCamera();
        });
        host.addEventListener("wheel", function (e) {
            e.preventDefault();
            sph.r = Math.max(0.05, sph.r * (1 + (e.deltaY > 0 ? 0.12 : -0.12)));
            applyCamera();
        }, { passive: false });
        host.addEventListener("click", onClick);
    }

    function pan(dx, dy) {
        var scale = sph.r * 0.0016;
        var right = new THREE.Vector3().crossVectors(new THREE.Vector3().subVectors(target, camera.position).normalize(), camera.up).normalize();
        var up = new THREE.Vector3().crossVectors(right, new THREE.Vector3().subVectors(target, camera.position).normalize()).normalize();
        target.addScaledVector(right, -dx * scale);
        target.addScaledVector(up, dy * scale);
    }

    function onClick(e) {
        if (!pickables.length) return;
        var host = document.getElementById("scene"), r = host.getBoundingClientRect();
        var ndc = new THREE.Vector2(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
        raycaster.setFromCamera(ndc, camera);
        var hits = raycaster.intersectObjects(pickables, false);
        if (hits.length) post({ type: "pick", name: hits[0].object.userData.name });
    }

    // ---- rendering ----------------------------------------------------------------------------
    function clearGroup(g) {
        for (var i = g.children.length - 1; i >= 0; i--) {
            var o = g.children[i];
            if (o.geometry) o.geometry.dispose();
            if (o.material) o.material.dispose();
            g.remove(o);
        }
    }

    window.stClear = function () {
        if (planeGroup) clearGroup(planeGroup);
        if (lineGroup) clearGroup(lineGroup);
        pickables = [];
        document.getElementById("empty").classList.remove("hidden");
    };

    window.stRender = function (data) {
        try {
            if (typeof data === "string") data = JSON.parse(data);
            clearGroup(planeGroup); clearGroup(lineGroup); pickables = [];

            var box = new THREE.Box3();
            var has = false;

            // Cave main-line.
            var legs = data.legs || [];
            if (legs.length >= 6) {
                var pos = new Float32Array(legs);
                var geo = new THREE.BufferGeometry();
                geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
                lineGroup.add(new THREE.LineSegments(geo, new THREE.LineBasicMaterial({ color: 0x888888 })));
                box.expandByObject(lineGroup); has = true;
            }

            // Planes as oriented translucent discs (+ a normal stub).
            var zaxis = new THREE.Vector3(0, 0, 1);
            (data.planes || []).forEach(function (p, i) {
                if (!p.valid) return;
                var c = new THREE.Vector3(p.centroid[0], p.centroid[1], p.centroid[2]);
                var n = new THREE.Vector3(p.normal[0], p.normal[1], p.normal[2]).normalize();
                var radius = Math.max(0.25, p.radius || 1);
                var color = new THREE.Color(p.color || PALETTE[i % PALETTE.length]);

                var disc = new THREE.Mesh(
                    new THREE.CircleGeometry(radius, 48),
                    new THREE.MeshLambertMaterial({ color: color, transparent: true, opacity: 0.45, side: THREE.DoubleSide, depthWrite: false }));
                disc.quaternion.setFromUnitVectors(zaxis, n);
                disc.position.copy(c);
                disc.userData.name = p.name;
                planeGroup.add(disc);
                pickables.push(disc);

                var ring = new THREE.Mesh(
                    new THREE.RingGeometry(radius * 0.98, radius, 48),
                    new THREE.MeshBasicMaterial({ color: color, side: THREE.DoubleSide }));
                ring.quaternion.copy(disc.quaternion); ring.position.copy(c);
                planeGroup.add(ring);

                var nl = new THREE.BufferGeometry().setFromPoints([c, c.clone().addScaledVector(n, radius * 0.8)]);
                planeGroup.add(new THREE.Line(nl, new THREE.LineBasicMaterial({ color: color })));

                box.expandByPoint(c); has = true;
            });

            document.getElementById("empty").classList.toggle("hidden", has);
            if (has) fit(box);
        } catch (e) { post({ type: "console", level: "error", message: "stRender failed: " + e }); }
    };

    function fit(box) {
        if (box.isEmpty()) return;
        var center = box.getCenter(new THREE.Vector3());
        var size = box.getSize(new THREE.Vector3()).length();
        target.copy(center);
        sph.r = Math.max(1, size * 1.3);
        applyCamera();
    }
    window.stFit = function () { var b = new THREE.Box3(); b.expandByObject(lineGroup); b.expandByObject(planeGroup); fit(b); };

    // C# sets the scene background ("#1e1e1e" dark / "#ffffff" white).
    window.stSetBackground = function (hex) {
        try {
            bgColor = (typeof hex === "string") ? new THREE.Color(hex).getHex() : hex;
            if (scene) scene.background = new THREE.Color(bgColor);
        } catch (e) { /* ignore */ }
    };

    // Capture the current view as a PNG data-URL and hand it back to C# for saving.
    window.stExport = function () {
        try {
            var host = document.getElementById("scene");
            renderer.setScissorTest(false);
            renderer.setViewport(0, 0, host.clientWidth, host.clientHeight);
            renderer.clear();
            renderer.render(scene, camera);
            renderGizmo();
            post({ type: "image", data: renderer.domElement.toDataURL("image/png") });
        } catch (e) { post({ type: "console", level: "error", message: "export failed: " + e }); }
    };

    if (window.THREE) init();
    else post({ type: "console", level: "error", message: "three.js failed to load" });
})();
