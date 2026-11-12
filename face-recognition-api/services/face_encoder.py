"""Face encoding helpers using the insightface library (no dlib/CMake needed)."""

import json

import cv2
import numpy as np

from services.face_analysis import get_face_analysis


class FaceEncodeError(Exception):
    """Raised when no face is detected in the provided image."""


def _to_image(image_bytes: bytes) -> np.ndarray:
    """Decode raw image bytes into a BGR numpy array ready for insightface."""
    image = cv2.imdecode(np.frombuffer(image_bytes, np.uint8), cv2.IMREAD_COLOR)
    if image is None:
        raise FaceEncodeError("Could not decode the provided image.")
    return image


def encode_from_bytes(image_bytes: bytes) -> str:
    """Detect a face in the image and return its embedding as a JSON string.

    Raises FaceEncodeError if no face is found.
    """
    app = get_face_analysis()
    faces = app.get(_to_image(image_bytes))

    if not faces:
        raise FaceEncodeError("No face detected in the provided image.")

    if len(faces) > 1:
        raise FaceEncodeError("Multiple faces detected. Please ensure only one face is visible.")

    embedding = faces[0].normed_embedding
    return json.dumps(embedding.astype(float).tolist())


def decode_encoding(encoding_str: str) -> np.ndarray:
    """Convert a stored encoding JSON string back into a numpy array."""
    return np.array(json.loads(encoding_str), dtype=np.float32)
