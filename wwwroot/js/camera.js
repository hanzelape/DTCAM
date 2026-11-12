// camera.js - Manages browser webcam access for photo capture.

(function () {
    "use strict";

    let stream = null;
    let videoElement = null;
    let canvasElement = null;
    let trackingCanvas = null;
    let trackingContext = null;
    let faceTrackingTimer = null;
    const FACE_DETECTION_INTERVAL_MS = 150;

    function initCamera(videoId, canvasId) {
        videoElement = document.getElementById(videoId);
        canvasElement = document.getElementById(canvasId);
    }

    // Human-readable reason for a getUserMedia failure. Callers need this
    // because the browser error names are not actionable for an end user.
    function describeCameraError(err) {
        const name = err && err.name ? err.name : "";

        switch (name) {
            case "NotAllowedError":
            case "SecurityError":
                return "Camera permission was denied. Allow camera access in your browser's site settings, then press Start again.";
            case "NotFoundError":
            case "DevicesNotFoundError":
                return "No camera was found on this device.";
            case "NotReadableError":
            case "TrackStartError":
                return "The camera is already in use by another application. Close it and try again.";
            case "OverconstrainedError":
                return "No camera matching the requested settings was found.";
            case "AbortError":
                return "The camera could not be started. Please try again.";
            default:
                return "Could not access the camera. Check that it is connected and that permission is allowed.";
        }
    }

    // True only if the browser exposes a usable camera API at all.
    function isSupported() {
        return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
    }

    async function startCamera(videoId, canvasId) {
        initCamera(videoId, canvasId);

        if (!videoElement) {
            return { ok: false, reason: "The video element was not found on this page." };
        }

        if (!isSupported()) {
            return {
                ok: false,
                // getUserMedia is undefined on an insecure origin, which is
                // the single most common cause and worth naming explicitly.
                reason: window.isSecureContext === false
                    ? "Camera access requires a secure (https) connection or localhost."
                    : "This browser does not support camera access."
            };
        }

        // A second camera already open means a previous start() was never
        // stopped — release it rather than leaking the device.
        if (stream) {
            stopCamera();
        }

        try {
            stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: "user", width: { ideal: 640 }, height: { ideal: 480 } },
                audio: false
            });

            videoElement.srcObject = stream;

            // Autoplay can be rejected outright; surface that rather than
            // letting the rejection escape as an unhandled promise.
            try {
                await videoElement.play();
            } catch (playErr) {
                if (playErr && playErr.name === "NotAllowedError") {
                    return {
                        ok: false,
                        reason: "The browser blocked video playback. Press Start again to allow it."
                    };
                }
                throw playErr;
            }

            // Some drivers resolve play() before any frame is decoded, which
            // makes capture() return null. Wait briefly for real dimensions.
            if (!videoElement.videoWidth) {
                await new Promise(function (resolve) {
                    const done = function () {
                        videoElement.removeEventListener("loadedmetadata", done);
                        resolve();
                    };
                    videoElement.addEventListener("loadedmetadata", done);
                    setTimeout(done, 2000);
                });
            }

            if (!videoElement.videoWidth) {
                return { ok: false, reason: "The camera started but produced no video. Try another camera." };
            }

            initFaceTracking();
            if (!startFaceTracking()) {
                stopCamera();
                return { ok: false, reason: "Face detection could not be initialized. Please try again." };
            }
            syncTrackingCanvas();

            return { ok: true, reason: "" };
        } catch (err) {
            console.error("Camera error:", err);
            // Release a partially opened stream so the device light goes off.
            if (stream) {
                stream.getTracks().forEach(function (track) { track.stop(); });
                stream = null;
            }
            if (videoElement) {
                videoElement.srcObject = null;
            }
            return { ok: false, reason: describeCameraError(err) };
        }
    }

    // Captures one frame and returns a Base64 data URL.
    function captureFrame() {
        if (!videoElement || !canvasElement) return null;

        const width = videoElement.videoWidth;
        const height = videoElement.videoHeight;

        if (width === 0 || height === 0) {
            console.warn("capture() called with no video dimensions — camera not running.");
            return null;
        }

        canvasElement.width = width;
        canvasElement.height = height;

        const ctx = canvasElement.getContext("2d");
        ctx.drawImage(videoElement, 0, 0, width, height);

        return canvasElement.toDataURL("image/jpeg", 0.9);
    }

    // True while a stream is actually held.
    function isRunning() {
        return !!stream;
    }

    // The tracking canvas is only shown while a feed is live. Without this
    // the last frame of face boxes stays painted over the captured still.
    function syncTrackingCanvas() {
        if (!videoElement) return;
        const canvas = document.getElementById('faceCanvas');
        if (!canvas) return;

        if (stream && faceTrackingActive) {
            canvas.classList.add('is-tracking');
        } else {
            canvas.classList.remove('is-tracking');
            // Drop whatever was last drawn so nothing lingers.
            const ctx = canvas.getContext('2d');
            if (ctx) ctx.clearRect(0, 0, canvas.width, canvas.height);
        }
    }

    function stopCamera() {
        stopFaceTracking();
        syncTrackingCanvas();
        if (stream) {
            stream.getTracks().forEach(track => track.stop());
            stream = null;
        }

        if (videoElement) {
            videoElement.srcObject = null;
        }
    }

let faceTracker = null;
let faceTrackingActive = false;
let onTrackCallback = null;
let onTrackingErrorCallback = null;

    function initFaceTracking() {
        if (typeof tracking === 'undefined' || !tracking.ObjectTracker) {
            console.warn('tracking.js not loaded.');
            return;
        }

        try {
            faceTracker = new tracking.ObjectTracker('face');
            faceTracker.setInitialScale(4);
            faceTracker.setStepSize(2);
            faceTracker.setEdgesDensity(0.1);
        } catch (e) {
            console.error('Failed to initialize face tracker:', e);
        }
    }

    function startFaceTracking() {
        if (!faceTracker) {
            console.error('Face tracker not initialized. tracking.js loaded:', typeof tracking !== 'undefined');
            return false;
        }

        const canvas = document.getElementById('faceCanvas');
        if (!videoElement || !canvas) return false;

        faceTrackingActive = true;
        trackingCanvas = document.createElement('canvas');
        trackingContext = trackingCanvas.getContext('2d', { willReadFrequently: true });
        const trackingStartedAt = performance.now();
        let firstFaceDetectionLogged = false;

        faceTracker.on('track', function(event) {
            if (!faceTrackingActive) return;

            if (!firstFaceDetectionLogged && event.data.length > 0) {
                firstFaceDetectionLogged = true;
                console.info('[performance] first_face_detection_ms=' +
                    Math.round(performance.now() - trackingStartedAt));
            }

            // Forward detected faces to any consumer (e.g. the registration
            // countdown state machine) without disturbing the box rendering.
            if (typeof onTrackCallback === 'function') {
                try { onTrackCallback(event.data); } catch (e) { console.error('onTrack callback:', e); }
            }

            const ctx = canvas.getContext('2d');
            const rect = videoElement.getBoundingClientRect();

            if (rect.width === 0 || rect.height === 0) {
                return;
            }

            canvas.width = rect.width;
            canvas.height = rect.height;

            ctx.clearRect(0, 0, canvas.width, canvas.height);

            event.data.forEach(function(face) {
                ctx.strokeStyle = '#1e5eff';
                ctx.lineWidth = 4;
                ctx.fillStyle = 'rgba(30, 94, 255, 0.15)';
                ctx.fillRect(face.x, face.y, face.width, face.height);
                ctx.strokeRect(face.x, face.y, face.width, face.height);
            });
        });

        function processFrame() {
            if (!faceTrackingActive) return;

            if (videoElement.hidden || videoElement.readyState < videoElement.HAVE_ENOUGH_DATA ||
                videoElement.videoWidth === 0 || videoElement.videoHeight === 0) {
                faceTrackingTimer = setTimeout(processFrame, FACE_DETECTION_INTERVAL_MS);
                return;
            }

            const width = videoElement.offsetWidth;
            const height = videoElement.offsetHeight;
            if (width === 0 || height === 0 || !trackingContext) {
                faceTrackingTimer = setTimeout(processFrame, FACE_DETECTION_INTERVAL_MS);
                return;
            }

            if (trackingCanvas.width !== width || trackingCanvas.height !== height) {
                trackingCanvas.width = width;
                trackingCanvas.height = height;
            }

            try {
                trackingContext.drawImage(videoElement, 0, 0, width, height);
                const frame = trackingContext.getImageData(0, 0, width, height);
                faceTracker.track(frame.data, width, height);
            } catch (error) {
                console.error('Face tracking frame failed:', error);
                if (typeof onTrackingErrorCallback === 'function') {
                    onTrackingErrorCallback();
                }
                stopFaceTracking();
                return;
            }

            if (faceTrackingActive) {
                faceTrackingTimer = setTimeout(processFrame, FACE_DETECTION_INTERVAL_MS);
            }
        }

        faceTrackingTimer = setTimeout(processFrame, 0);
        return true;
    }

    function stopFaceTracking() {
        faceTrackingActive = false;
        if (faceTrackingTimer) {
            clearTimeout(faceTrackingTimer);
            faceTrackingTimer = null;
        }
        trackingCanvas = null;
        trackingContext = null;

        const canvas = document.getElementById('faceCanvas');
        if (canvas) {
            const ctx = canvas.getContext('2d');
            ctx.clearRect(0, 0, canvas.width, canvas.height);
        }

    }

    window.Camera = {
        start: startCamera,
        capture: captureFrame,
        stop: stopCamera,
        isRunning: isRunning,
        isSupported: isSupported,
        describeError: describeCameraError,
        startFaceTracking: startFaceTracking,
        stopFaceTracking: stopFaceTracking,
        setOnTrack: function (cb) { onTrackCallback = cb; },
        setOnTrackingError: function (cb) { onTrackingErrorCallback = cb; }
    };
})();
