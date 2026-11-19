// attendance.js - Kiosk face check-in with a head-turn liveness challenge.
// People check in one after another; an attempt starts only when the
// on-device face tracker sees a face, so an empty kiosk sends nothing.

(function () {
    "use strict";

    const messageArea = document.getElementById('messageArea');
    const resultContainer = document.getElementById('resultContainer');

    const FACE_RECENT_MS = 600;
    const PAUSE_AFTER_SUCCESS_MS = 3000;
    const PAUSE_AFTER_FAILURE_MS = 2500;
    const PAUSE_AFTER_RATE_LIMIT_MS = 30000;

    let running = false;
    let lastFaceSeenAt = 0;
    let promptEl = null;

    function showMessage(text, isError) {
        if (!messageArea) return;
        messageArea.textContent = text;
        messageArea.className = isError
            ? 'alert alert-danger mt-3'
            : 'alert alert-success mt-3';
        messageArea.hidden = false;
    }

    function ensurePromptElement() {
        if (promptEl) return promptEl;
        const viewport = document.getElementById('checkinViewport');
        if (!viewport) return null;
        promptEl = document.createElement('div');
        promptEl.className = 'liveness-prompt';
        promptEl.setAttribute('role', 'status');
        promptEl.setAttribute('aria-live', 'assertive');
        promptEl.hidden = true;
        viewport.appendChild(promptEl);
        return promptEl;
    }

    function setPrompt(text) {
        const el = ensurePromptElement();
        if (!el) return;
        el.textContent = text || '';
        el.hidden = !text;
    }

    function addResultCard(data) {
        if (!resultContainer) return;

        const card = document.createElement('div');
        card.className = 'checkin-result-card';
        card.innerHTML = `
            <div class="checkin-result-header">
                <div class="checkin-result-icon">
                    <i class="bi bi-check2"></i>
                </div>
                <h6>Check-in Successful</h6>
            </div>
            <div class="checkin-result-meta">
                <div><strong>Name:</strong> <span data-name></span></div>
                <div><strong>Schedule:</strong> <span data-schedule></span></div>
                <div><strong>Status:</strong> <span data-status></span></div>
                <div><strong>Detection reliability:</strong> <span data-confidence></span></div>
            </div>
            <div class="checkin-result-time"><small></small></div>
        `;
        // textContent, not innerHTML: names come from the database.
        card.querySelector('[data-name]').textContent = data.employeeName || '';
        card.querySelector('[data-schedule]').textContent = data.schedule || '';
        card.querySelector('[data-status]').textContent = data.status || '';
        card.querySelector('[data-confidence]').textContent = data.confidence ? (data.confidence * 100).toFixed(1) + '%' : '';
        card.querySelector('small').textContent = new Date().toLocaleTimeString();
        resultContainer.appendChild(card);

        resultContainer.scrollTop = resultContainer.scrollHeight;
    }

    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

    async function attemptOnce() {
        const video = document.getElementById('videoCamera');
        const data = await window.LivenessCheckIn.run({
            challengeUrl: '/Attendance/LivenessChallenge',
            recognizeUrl: '/Attendance/Recognize',
            video: video,
            prompt: setPrompt
        });

        if (data.success) {
            showMessage(data.message, false);
            addResultCard(data);
            return PAUSE_AFTER_SUCCESS_MS;
        }

        showMessage(data.message || 'Face not recognized. Please try again.', true);

        if (data.unauthorized) {
            stopScanning();
            setTimeout(function () { window.location.href = '/Account/Login'; }, 1500);
            return 0;
        }
        return data.rateLimited ? PAUSE_AFTER_RATE_LIMIT_MS : PAUSE_AFTER_FAILURE_MS;
    }

    async function loop() {
        while (running) {
            if (Date.now() - lastFaceSeenAt > FACE_RECENT_MS) {
                await sleep(200);
                continue;
            }
            const pause = await attemptOnce();
            if (running && pause) {
                setPrompt(null);
                await sleep(pause);
            }
        }
    }

    function startScanning() {
        if (running) return;
        running = true;
        lastFaceSeenAt = 0;
        if (window.Camera && window.Camera.setOnTrack) {
            window.Camera.setOnTrack(function (faces) {
                if (faces && faces.length > 0) lastFaceSeenAt = Date.now();
            });
        }
        showMessage('Step in front of the camera to check in.', false);
        loop();
    }

    function stopScanning() {
        running = false;
        setPrompt(null);
    }

    window.Attendance = {
        startScanning: startScanning,
        stopScanning: stopScanning,
        submit: attemptOnce
    };
})();
