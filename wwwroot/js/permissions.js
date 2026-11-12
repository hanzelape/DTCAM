// permissions.js — camera and location access, requested at the point of use.
//
// Behaviour required:
//   * Once per login session, a popup offers both camera and location.
//   * If the user agrees, camera features and map features stop asking.
//   * If they decline (or dismiss), the popup returns the next time a
//     camera feature or a map feature is opened.
//
// Session state lives in sessionStorage, so a fresh login re-asks while
// ordinary navigation inside the same session does not. The browser's own
// permission state is always treated as authoritative when it is readable.

(function () {
    'use strict';

    var STORE_KEY = 'vs-perms';

    var panel = null;
    var reasonEl = null;
    var statusEl = null;
    var allowBtn = null;
    var rowsEl = null;
    var busy = false;
    var bound = false;

    // ---------------------------------------------------------------- state

    function readState() {
        try {
            var raw = sessionStorage.getItem(STORE_KEY);
            return raw ? (JSON.parse(raw) || {}) : {};
        } catch (e) {
            return {};
        }
    }

    function writeState(patch) {
        try {
            var next = readState();
            Object.keys(patch).forEach(function (k) { next[k] = patch[k]; });
            sessionStorage.setItem(STORE_KEY, JSON.stringify(next));
        } catch (e) { /* private mode — state is simply not remembered */ }
    }

    function wasGranted(kind) {
        return readState()[kind] === 'granted';
    }

    // ------------------------------------------------------------- probing

    // A permission alone does not start a camera, and there is no way to
    // raise the browser's own prompt without asking for the device. So take
    // the stream and immediately release it.
    function probeCamera() {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            return Promise.resolve(false);
        }
        return navigator.mediaDevices.getUserMedia({ video: true, audio: false })
            .then(function (stream) {
                stream.getTracks().forEach(function (t) { t.stop(); });
                return true;
            })
            .catch(function () { return false; });
    }

    function probeLocation() {
        if (!navigator.geolocation) return Promise.resolve(false);
        return new Promise(function (resolve) {
            navigator.geolocation.getCurrentPosition(
                function () { resolve(true); },
                function () { resolve(false); },
                { enableHighAccuracy: false, timeout: 12000, maximumAge: 0 }
            );
        });
    }

    var PROBES = { camera: probeCamera, location: probeLocation };
    var LABELS = {
        camera: 'Camera',
        location: 'Location',
        cameraDetail: 'Used to capture and recognise faces.',
        locationDetail: 'Used to verify you are at the attendance site.'
    };

    // Reads the browser's own state where available. Firefox and Safari do
    // not expose geolocation here, so 'unknown' means "ask and find out".
    function browserState(kind) {
        var name = kind === 'camera' ? 'camera' : 'geolocation';
        if (!navigator.permissions || !navigator.permissions.query) {
            return Promise.resolve('unknown');
        }
        return navigator.permissions.query({ name: name })
            .then(function (s) { return s.state; })
            .catch(function () { return 'unknown'; });
    }

    // ------------------------------------------------------------------ UI

    function cacheParts() {
        if (panel) return true;
        panel = document.getElementById('cameraPermissionPrompt');
        if (!panel) return false;
        reasonEl = panel.querySelector('[data-cp-reason]');
        statusEl = panel.querySelector('[data-cp-status]');
        allowBtn = panel.querySelector('[data-cp-allow]');
        rowsEl = panel.querySelector('[data-cp-rows]');
        return true;
    }

    function renderRows(kinds) {
        if (!rowsEl) return;
        rowsEl.innerHTML = kinds.map(function (k) {
            var icon = k === 'camera' ? 'camera-video' : 'geo-alt';
            return '<div class="camera-permission-row" data-kind="' + k + '">' +
                '<i class="bi bi-' + icon + ' camera-permission-row-icon" aria-hidden="true"></i>' +
                '<div class="camera-permission-row-text">' +
                '<span class="camera-permission-row-label">' + LABELS[k] + '</span>' +
                '<span class="camera-permission-row-detail">' + LABELS[k + 'Detail'] + '</span>' +
                '</div></div>';
        }).join('');
    }

    function setStatus(text, tone) {
        if (!statusEl) return;
        statusEl.textContent = text || '';
        statusEl.className = 'camera-permission-status' + (tone ? ' is-' + tone : '');
    }

    function show(kinds, reason) {
        // Remembered so the Allow button knows what it is asking for.
        panel.__kinds = kinds.slice();
        renderRows(kinds);
        if (reasonEl) reasonEl.textContent = reason || 'DTCAM needs access to continue.';
        if (allowBtn) {
            allowBtn.textContent = kinds.length > 1 ? 'Allow access' : 'Allow ' + LABELS[kinds[0]].toLowerCase();
        }

        var hardBlocked = kinds.filter(function (k) {
            return k === 'camera' && navigator.mediaDevices === undefined;
        });

        if (hardBlocked.length) {
            setStatus(window.isSecureContext === false
                ? 'Camera access requires a secure (https) connection or localhost.'
                : 'This browser does not provide camera access.', 'error');
        } else {
            setStatus('Your browser will ask you to approve each one.', '');
        }

        panel.classList.add('is-visible');
    }

    function hide() {
        if (panel) panel.classList.remove('is-visible');
    }

    var settle = null;

    function finish(value) {
        var resolve = settle;
        settle = null;
        busy = false;
        if (allowBtn) allowBtn.disabled = false;
        if (resolve) resolve(value);
    }

    function prompt(kinds, reason) {
        return new Promise(function (resolve) {
            settle = resolve;
            show(kinds, reason);
        });
    }

    function ask(kinds) {
        busy = true;
        if (allowBtn) allowBtn.disabled = true;
        setStatus('Waiting for your browser…', '');

        // Sequential on purpose: two simultaneous browser permission prompts
        // race, and the second is silently dropped on some platforms.
        var results = {};
        var chain = Promise.resolve();

        kinds.forEach(function (kind) {
            chain = chain.then(function () {
                return PROBES[kind]().then(function (ok) {
                    results[kind] = ok;
                });
            });
        });

        return chain.then(function () {
            var patch = {};
            kinds.forEach(function (k) { patch[k] = results[k] ? 'granted' : 'denied'; });
            writeState(patch);

            var failed = kinds.filter(function (k) { return !results[k]; });

            if (failed.length === 0) {
                hide();
                finish(results);
            } else {
                busy = false;
                if (allowBtn) allowBtn.disabled = false;
                var names = failed.map(function (k) { return LABELS[k]; }).join(' and ');
                setStatus(names + ' access was not granted. You can still use the app — it will ask again when a feature needs it.', 'error');

                // Settle so the caller is never left waiting on a promise
                // that only resolves on the next Allow click.
                finish(results);
            }
        });
    }

    // -------------------------------------------------------------- public

    /**
     * Resolve truthy only if every requested kind is available.
     * Re-prompts each time it is called while something is still missing.
     */
    function ensure(kinds, reason) {
        var list = Array.isArray(kinds) ? kinds : [kinds];
        if (!cacheParts()) {
            // No popup on this page: let the caller try for itself and let
            // its own error handling report the failure.
            return Promise.resolve({});
        }

        attachPromptHandlers();

        var missing = list.filter(function (kind) { return !wasGranted(kind); });

        if (missing.length === 0) {
            return Promise.resolve(list.reduce(function (acc, k) { acc[k] = true; return acc; }, {}));
        }

        // Already showing: answer the existing prompt rather than stacking.
        if (settle) return settle;

        return browserState(missing[0]).then(function (state) {
            if (state === 'granted') {
                var patch = {};
                patch[missing[0]] = 'granted';
                writeState(patch);
                // Re-check the remaining kinds in the same pass.
                return ensure(missing.slice(1), reason);
            }
            // 'denied' still shows the popup: the user still needs the
            // browser-settings instructions, since only they can undo it.
            return prompt(missing, reason).then(function (res) {
                return list.reduce(function (acc, k) { acc[k] = !!res[k]; return acc; }, {});
            });
        });
    }

    /**
     * First popup of a login session, offering camera and location together.
     * sessionStorage guards against repeating it on every navigation.
     */
    function promptOnLoad() {
        if (!cacheParts()) return;
        if (readState().prompted === '1') return;

        writeState({ prompted: '1' });
        attachPromptHandlers();

        var wanted = ['camera', 'location'].filter(function (kind) { return !wasGranted(kind); });
        if (wanted.length === 0) return;

        prompt(wanted, 'Grant access so camera and map features work. You can skip this and be asked again later.');
    }

    function attachPromptHandlers() {
        if (bound) return;
        bound = true;

        allowBtn.addEventListener('click', function () {
            if (busy || !settle) return;
            // Kinds currently rendered are authoritative.
            var kinds = rowsEl ? Array.from(rowsEl.querySelectorAll('[data-kind]')).map(function (el) {
                return el.getAttribute('data-kind');
            }) : [];
            if (!kinds.length) kinds = panel.__kinds || [];
            if (!kinds.length) { finish({}); return; }
            ask(kinds);
        });

        panel.querySelectorAll('[data-cp-dismiss]').forEach(function (el) {
            el.addEventListener('click', function () {
                hide();
                finish({});
            });
        });

        // Clicking the backdrop (but not the card) dismisses.
        panel.addEventListener('click', function (e) {
            if (e.target === panel) {
                hide();
                finish({});
            }
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && panel.classList.contains('is-visible')) {
                hide();
                finish({});
            }
        });
    }

    window.Permissions = {
        ensure: ensure,
        promptOnLoad: promptOnLoad
    };
})();
