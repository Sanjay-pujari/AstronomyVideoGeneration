from fastapi import FastAPI
from pydantic import BaseModel
from typing import List
app = FastAPI(title="Astronomy Skyfield Sidecar")
class VisibilityRequest(BaseModel):
    date: str
    latitude: float
    longitude: float
    elevation_m: float = 0.0
    targets: List[str]
@app.get("/health")
def health(): return {"status": "ok"}
@app.post("/api/visibility")
def visibility(req: VisibilityRequest):
    items = []
    for target in req.targets:
        items.append({"target": target, "bestTimeLocal": "21:00", "altitudeDegrees": 48.5, "azimuthDegrees": 192.3, "visibility": "Good", "notes": "Stub response from sidecar; replace with real Skyfield logic."})
    return {"date": req.date, "items": items}
