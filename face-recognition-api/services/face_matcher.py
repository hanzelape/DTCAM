"""Face matching logic for the /recognize endpoint (cosine similarity)."""

import numpy as np

from services.face_analysis import get_face_analysis
from services.face_encoder import _to_image, decode_encoding


class FaceMatchResult:
    def __init__(self, recognized, employee_id=None, employee_name=None, confidence=0.0):
        self.recognized = recognized
        self.employee_id = employee_id
        self.employee_name = employee_name
        self.confidence = confidence

    def to_dict(self):
        return {
            "recognized": self.recognized,
            "employee_id": self.employee_id,
            "employee_name": self.employee_name,
            "confidence": round(float(self.confidence), 4),
        }


def match_face(image_bytes: bytes, known_faces: list, threshold: float = 0.60) -> FaceMatchResult:
    """Match a captured face against a list of known face embeddings.

    known_faces: list of dicts with keys: employee_id, employee_name, encoding (JSON string)
    threshold: minimum cosine similarity to accept a match. Typical values 0.45-0.65.
    """
    app = get_face_analysis()
    faces = app.get(_to_image(image_bytes))

    if not faces:
        return FaceMatchResult(recognized=False, confidence=0.0)

    if len(faces) > 1:
        return FaceMatchResult(recognized=False, confidence=0.0)

    return match_encoding(faces[0].normed_embedding, known_faces, threshold)


def match_encoding(unknown_embedding: np.ndarray, known_faces: list, threshold: float = 0.60) -> FaceMatchResult:
    """Match an already-computed face embedding against known embeddings."""
    if not known_faces:
        return FaceMatchResult(recognized=False, confidence=0.0)

    unknown_embedding = np.asarray(unknown_embedding, dtype=np.float32).reshape(1, -1)
    known_encs = np.array([decode_encoding(face["encoding"]) for face in known_faces], dtype=np.float32)
    known_ids = [face["employee_id"] for face in known_faces]
    known_names = [face["employee_name"] for face in known_faces]

    unknown_norm = np.linalg.norm(unknown_embedding, axis=1, keepdims=True) + 1e-9
    known_norms = np.linalg.norm(known_encs, axis=1, keepdims=True) + 1e-9
    similarities = (unknown_embedding @ known_encs.T) / (unknown_norm * known_norms.T)
    similarities = similarities.flatten()

    best_index = int(np.argmax(similarities))
    best_similarity = float(similarities[best_index])

    if best_similarity >= threshold:
        return FaceMatchResult(
            recognized=True,
            employee_id=known_ids[best_index],
            employee_name=known_names[best_index],
            confidence=best_similarity,
        )

    return FaceMatchResult(recognized=False, confidence=best_similarity)
