// liveness.js - Head-turn liveness check-in shared by the employee check-in
// page and the admin kiosk.
//
// One attempt:
//   1. read a fresh GPS fix (lat, lng, accuracy) - required by the server
//   2. POST the challenge URL -> server picks "left" or "right" (single use, 60 s)
//   3. "Look straight" -> capture frontal frames
//   4. "Turn your head to your LEFT/RIGHT" -> capture the turn
//   5. POST all frames + challenge id + location to the recognize URL
//
// The server (and the Python API) decide whether the turn really happened;
// nothing here is trusted.

(function () {
    "use strict";

    const FRONTAL_FRAMES = 3;
    const TURN_FRAMES = 7;
    const FRAME_INTERVAL_MS = 300;
    const SETTLE_MS = 700;
    const MAX_FRAME_WIDTH = 480;
    const JPEG_QUALITY = 0.8;

    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

    function antiForgeryToken() {
        const input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function jsonHeaders() {
        return {
            'Content-Type': 'application/json',
            'Accept': 'application/json',
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': antiForgeryToken()
        };
    }

    // Fresh, high-accuracy fix. Resolves { position } or { error }.
    function getPosition() {
        return new Promise(function (resolve) {
            if (!navigator.geolocation) {
                resolve({ error: 'This browser cannot share your location, which is required to check in.' });
                return;
            }
            navigator.geolocation.getCurrentPosition(
                function (pos) {
                    resolve({
                        position: {
                            latitude: pos.coords.latitude,
                            longitude: pos.coords.longitude,
                            accuracy: pos.coords.accuracy
                        }
                    });
                },
                function (err) {
                    const denied = err && err.code === 1;
                    resolve({
                        error: denied
                            ? 'Location permission is required to check in. Allow location access and try again.'
                            : 'Could not get your location. Make sure location / GPS is on and try again.'
                    });
                },
                { enableHighAccuracy: true, timeout: 10000, maximumAge: 15000 }
            );
        });
    }

    // Downscaled JPEG straight from the <video> (keeps the upload small).
    function captureFrame(video, canvas) {
        const w = video.videoWidth;
        const h = video.videoHeight;
        if (!w || !h) return null;
        const scale = Math.min(1, MAX_FRAME_WIDTH / w);
        canvas.width = Math.round(w * scale);
        canvas.height = Math.round(h * scale);
        canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
        return canvas.toDataURL('image/jpeg', JPEG_QUALITY);
    }

    async function captureBurst(video, canvas, count, frames) {
        for (let i = 0; i < count; i++) {
            const frame = captureFrame(video, canvas);
            if (frame) frames.push(frame);
            await sleep(FRAME_INTERVAL_MS);
        }
    }

    // Maps non-2xx responses to a result the page can show.
    async function readResponse(response) {
        if (response.status === 401) {
            return { success: false, unauthorized: true, message: 'Your session has expired. Please log in again.' };
        }
        if (response.status === 429) {
            return { success: false, rateLimited: true, message: 'Too many attempts. Please wait a minute and try again.' };
        }
        let data = null;
        try { data = await response.json(); } catch (e) { /* not JSON */ }
        if (!response.ok) {
            return { success: false, message: (data && data.message) || 'The server rejected the request (' + response.status + ').' };
        }
        return data || { success: false, message: 'The server returned an invalid response.' };
    }

    /**
     * Runs one liveness check-in attempt.
     * options: { challengeUrl, recognizeUrl, video, prompt(text|null) }
     * Resolves the server's JSON ({ success, message, ... }) or a client-side error.
     */
    async function run(options) {
        const video = options.video;
        const prompt = options.prompt || function () { };
        const canvas = document.createElement('canvas');

        try {
            prompt('Getting your location...');
            const loc = await getPosition();
            if (loc.error) return { success: false, message: loc.error };

            const challengeResponse = await fetch(options.challengeUrl, { method: 'POST', headers: jsonHeaders(), body: '{}' });
            const challenge = await readResponse(challengeResponse);
            if (!challenge.challengeId) return challenge.success === false ? challenge : { success: false, message: 'Could not start the liveness check.' };

            const frames = [];
            prompt('Look straight at the camera');
            await sleep(SETTLE_MS);
            await captureBurst(video, canvas, FRONTAL_FRAMES, frames);

            const direction = challenge.direction === 'left' ? 'LEFT' : 'RIGHT';
            prompt('Now slowly turn your head to your ' + direction + (direction === 'LEFT' ? ' ←' : ' →'));
            await captureBurst(video, canvas, TURN_FRAMES, frames);

            prompt('Verifying...');
            const response = await fetch(options.recognizeUrl, {
                method: 'POST',
                headers: jsonHeaders(),
                body: JSON.stringify({
                    frames: frames,
                    challengeId: challenge.challengeId,
                    latitude: loc.position.latitude,
                    longitude: loc.position.longitude,
                    accuracy: loc.position.accuracy
                })
            });
            return await readResponse(response);
        } catch (err) {
            console.error(err);
            return { success: false, message: 'Could not reach the server. Please try again.' };
        } finally {
            prompt(null);
        }
    }

    window.LivenessCheckIn = { run: run };
})();
