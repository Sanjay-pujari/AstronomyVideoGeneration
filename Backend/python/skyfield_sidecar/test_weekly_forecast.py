from fastapi.testclient import TestClient

from app import app


client = TestClient(app)


def _payload():
    return {
        "regionId": "IN-RJ-UDAIPUR",
        "locationName": "Udaipur",
        "latitude": 24.5854,
        "longitude": 73.7125,
        "timezone": "Asia/Kolkata",
        "weekStartDate": "2026-05-22",
        "days": 7,
        "language": "en",
        "preferredObjectCodes": [],
        "includeMoonPhases": True,
        "includePlanets": True,
        "includeDeepSkyObjects": True,
        "includeMeteorShowers": True,
        "includeConjunctions": True,
        "includeBestViewingWindows": True,
    }


def test_weekly_sky_success_and_shape():
    response = client.post("/forecast/weekly-sky", json=_payload())
    assert response.status_code == 200
    body = response.json()
    assert body["success"] is True
    assert body["weekStartDate"] == "2026-05-22"
    assert body["weekEndDate"] == "2026-05-28"
    assert len(body["days"]) == 7
    assert len(body["weeklyHighlights"]) >= 1
    assert len(body["recommendedNights"]) >= 1
    assert body["bestPlanetOfWeek"] is not None
    assert body["bestPlanetOfWeek"]["objectCode"] != "MOON"
    assert any(h["highlightType"] == "best_overall_night" for h in body["weeklyHighlights"])
    max_altitudes = []
    best_times = []
    has_invisible = False
    has_no_conjunction = True
    for day in body["days"]:
        assert day["sunsetUtc"]
        assert day["sunriseUtc"]
        assert day["moonPhase"]
        assert day["moonIlluminationPercent"] >= 0
        assert len(day["visibleObjects"]) > 0
        if any(evt["eventType"] == "conjunction" for evt in day["events"]):
            has_no_conjunction = False
        for obj in day["visibleObjects"]:
            if obj["objectCode"] in {"MERCURY", "VENUS", "MARS", "JUPITER", "SATURN", "URANUS", "NEPTUNE"}:
                max_altitudes.append(obj["maxAltitudeDegrees"])
                best_times.append(obj["bestViewingTimeUtc"])
            if obj["visible"] is False:
                has_invisible = True
                assert obj["visibilityScore"] == 0
    assert len(set(max_altitudes)) > 1
    assert len(set(best_times)) > 1
    assert has_invisible
    assert has_no_conjunction


def test_weekly_sky_invalid_latitude():
    payload = _payload()
    payload["latitude"] = 91
    response = client.post("/forecast/weekly-sky", json=payload)
    assert response.status_code == 422


def test_weekly_sky_invalid_days():
    payload = _payload()
    payload["days"] = 15
    response = client.post("/forecast/weekly-sky", json=payload)
    assert response.status_code == 422
