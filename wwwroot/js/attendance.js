// attendance.js - Automatic face recognition check-in with consecutive logins.

(function () {
    "use strict";

    const messageArea = document.getElementById('messageArea');
    const resultContainer = document.getElementById('resultContainer');

    let isProcessing = false;
    let scanInterval = null;
    let lastRecognizedId = null;
    let cooldownUntil = 0;
    let cachedPosition = null;

    function showMessage(text, isError) {
        if (!messageArea) return;
        messageArea.textContent = text;
        messageArea.className = isError
            ? 'alert alert-danger mt-3'
            : 'alert alert-success mt-3';
        messageArea.hidden = false;
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
                <div><strong>Name:</strong> <span data-name>${data.employeeName || ''}</span></div>
                <div><strong>Schedule:</strong> <span data-schedule>${data.schedule || ''}</span></div>
                <div><strong>Status:</strong> <span data-status>${data.status || ''}</span></div>
                <div><strong>Detection reliability:</strong> <span data-confidence>${data.confidence ? (data.confidence * 100).toFixed(1) + '%' : ''}</span></div>
            </div>
            <div class="checkin-result-time"><small>${new Date().toLocaleTimeString()}</small></div>
        `;
        resultContainer.appendChild(card);

        resultContainer.scrollTop = resultContainer.scrollHeight;
    }

    function getCachedPosition() {
        return new Promise(function (resolve) {
            if (cachedPosition) {
                resolve(cachedPosition);
                return;
            }

            if (!navigator.geolocation) {
                resolve(null);
                return;
            }

            navigator.geolocation.getCurrentPosition(
                function (pos) {
                    cachedPosition = {
                        latitude: pos.coords.latitude,
                        longitude: pos.coords.longitude
                    };
                    resolve(cachedPosition);
                },
                function () {
                    resolve(null);
                },
                { enableHighAccuracy: true, timeout: 5000, maximumAge: 60000 }
            );
        });
    }

    async function submitCheckIn(imageDataUrl) {
        if (isProcessing) return;
        if (Date.now() < cooldownUntil) return;

        isProcessing = true;

        try {
            const position = await getCachedPosition();
            const payload = {
                image: imageDataUrl
            };

            if (position) {
                payload.latitude = position.latitude;
                payload.longitude = position.longitude;
            }

            const response = await fetch('/Attendance/Recognize', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                },
                body: JSON.stringify(payload)
            });

            const data = await response.json();

            if (data.success) {
                showMessage(data.message, false);
                addResultCard(data);

                if (data.employeeId && data.employeeId === lastRecognizedId) {
                    cooldownUntil = Date.now() + 3000;
                } else {
                    cooldownUntil = Date.now() + 1000;
                }

                lastRecognizedId = data.employeeId || null;
            } else {
                showMessage(data.message || 'Face not recognized. Continuing to scan...', true);
            }
        } catch (err) {
            console.error(err);
            showMessage('Could not reach the server. Please try again.', true);
        } finally {
            isProcessing = false;
        }
    }

    function startScanning() {
        stopScanning();
        cachedPosition = null;
        showMessage('Starting face detection...', false);
        lastRecognizedId = null;
        cooldownUntil = 0;
        scanInterval = setInterval(() => {
            const dataUrl = window.Camera ? window.Camera.capture() : null;
            if (!dataUrl) {
                showMessage('Could not capture image. Make sure the camera is running.', true);
                return;
            }
            submitCheckIn(dataUrl);
        }, 2000);
    }

    function stopScanning() {
        if (scanInterval) {
            clearInterval(scanInterval);
            scanInterval = null;
        }
    }

    window.Attendance = {
        startScanning: startScanning,
        stopScanning: stopScanning,
        submit: submitCheckIn
    };
})();
