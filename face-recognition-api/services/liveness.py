"""Active liveness check: a server-chosen head turn, measured with the 5 facial
keypoints buffalo_l's detector already returns (eyes, nose, mouth corners).

How it works
------------
For each frame we measure how far the nose sits from the midpoint between the
eyes, along the eye axis, normalised by the eye distance ("yaw offset"):

    offset ~ 0      -> looking straight at the camera
    offset > 0      -> nose shifted toward image-right = the user's LEFT
    offset < 0      -> nose shifted toward image-left  = the user's RIGHT

(getUserMedia frames are not mirrored, so image-right is the subject's left.)

We require a frontal frame first, then a later frame turned the requested
way, and every frame must show the same person (embedding similarity).

Why a photo fails: tilting or rotating a flat print moves all of its points
together, so the nose stays centred between the eyes. Only a real 3D head
produces the nose shift. The remaining gap is a replayed *video* of the right
person turning the right way; a passive anti-spoof model (e.g. MiniFASNet)
would cover that later.
"""

import os
from dataclasses import dataclass

import numpy as np

from services.face_analysis import get_face_analysis
from services.face_encoder import _to_image

FRONTAL_MAX_OFFSET = float(os.getenv("LIVENESS_FRONTAL_MAX_OFFSET", "0.10"))
TURNED_MIN_OFFSET = float(os.getenv("LIVENESS_TURNED_MIN_OFFSET", "0.22"))
SAME_PERSON_MIN_SIMILARITY = float(os.getenv("LIVENESS_SAME_PERSON_MIN_SIMILARITY", "0.40"))
MIN_VALID_FRAMES = int(os.getenv("LIVENESS_MIN_VALID_FRAMES", "4"))


@dataclass
class FrameAnalysis:
    index: int
    offset: float
    embedding: np.ndarray


@dataclass
class LivenessResult:
    passed: bool
    reason: str | None = None
    reference_embedding: np.ndarray | None = None


def yaw_offset(kps: np.ndarray) -> float:
    """Signed nose offset along the eye axis, in units of eye distance."""
    left_eye, right_eye, nose = kps[0], kps[1], kps[2]
    axis = right_eye - left_eye
    eye_dist_sq = float(axis @ axis)
    if eye_dist_sq < 1e-6:
        return 0.0
    mid = (left_eye + right_eye) / 2.0
    return float((nose - mid) @ axis) / eye_dist_sq


def _cosine(a: np.ndarray, b: np.ndarray) -> float:
    return float(a @ b) / (float(np.linalg.norm(a) * np.linalg.norm(b)) + 1e-9)


def check_liveness(frames: list[bytes], challenge: str) -> LivenessResult:
    """Run the head-turn challenge over an ordered burst of frames."""
    if challenge not in ("left", "right"):
        return LivenessResult(False, "Invalid liveness challenge.")

    app = get_face_analysis()
    analysed: list[FrameAnalysis] = []

    for i, frame in enumerate(frames):
        faces = app.get(_to_image(frame))
        if len(faces) > 1:
            return LivenessResult(False, "More than one face is visible. Only you should be in view.")
        if not faces:
            # A strongly turned head can drop out of detection; tolerate gaps.
            continue
        face = faces[0]
        if getattr(face, "kps", None) is None:
            continue
        analysed.append(FrameAnalysis(i, yaw_offset(np.asarray(face.kps, dtype=np.float32)), face.normed_embedding))

    if len(analysed) < MIN_VALID_FRAMES:
        return LivenessResult(False, "Your face was not visible clearly enough. Stay in frame and try again.")

    frontal_idx = next((k for k, f in enumerate(analysed) if abs(f.offset) <= FRONTAL_MAX_OFFSET), None)
    if frontal_idx is None:
        return LivenessResult(False, "Look straight at the camera first, then turn your head.")

    reference = analysed[frontal_idx]
    sign = 1.0 if challenge == "left" else -1.0
    turned = next(
        (f for f in analysed[frontal_idx + 1:] if sign * f.offset >= TURNED_MIN_OFFSET),
        None,
    )
    if turned is None:
        return LivenessResult(False, f"Head turn not detected. Turn your head to your {challenge} when asked.")

    for f in analysed:
        if _cosine(f.embedding, reference.embedding) < SAME_PERSON_MIN_SIMILARITY:
            return LivenessResult(False, "The face changed during the check. Please try again.")

    # Use the most frontal frame as the identity reference.
    best = min(analysed, key=lambda f: abs(f.offset))
    return LivenessResult(True, None, best.embedding)
