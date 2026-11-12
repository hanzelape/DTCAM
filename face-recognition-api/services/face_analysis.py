"""Shared insightface FaceAnalysis model initialisation."""

import os
import threading

import insightface

# Singleton handle so the (~300MB) model is loaded only once per process.
_APP = None
_APP_LOCK = threading.Lock()
_MODEL_DIR = os.path.join(os.path.dirname(os.path.dirname(__file__)), "models")


def get_face_analysis():
    """Return a lazily-initialised singleton FaceAnalysis app."""
    global _APP
    if _APP is None:
        with _APP_LOCK:
            if _APP is None:
                app = insightface.app.FaceAnalysis(
                    name="buffalo_l",
                    root=_MODEL_DIR,
                    providers=["CPUExecutionProvider"],
                    allowed_modules=["detection", "recognition"],
                )
                app.prepare(ctx_id=-1, det_size=(320, 320))
                _APP = app
    return _APP
