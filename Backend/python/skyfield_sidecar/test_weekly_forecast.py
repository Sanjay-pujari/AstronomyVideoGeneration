from fastapi.testclient import TestClient

from app import VisibleObjectForecastItem, app, get_ranked_visible_objects


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


def test_recommendations_and_highlights_exclude_invisible_objects():
    response = client.post("/forecast/weekly-sky", json=_payload())
    assert response.status_code == 200
    body = response.json()

    visibility_by_day = {
        day["date"]: {obj["objectCode"]: obj for obj in day["visibleObjects"]}
        for day in body["days"]
    }

    for night in body["recommendedNights"]:
        day_lookup = visibility_by_day[night["date"]]
        for code in night["bestObjects"]:
            obj = day_lookup[code]
            assert obj["visible"] is True
            assert obj["visibilityScore"] > 0

    for highlight in body["weeklyHighlights"]:
        if highlight["highlightType"] not in {"best_overall_night", "best_photography_night"}:
            continue
        code = highlight.get("objectCode")
        if not code:
            continue
        obj = visibility_by_day[highlight["date"]][code]
        assert obj["visible"] is True
        if highlight["highlightType"] == "best_overall_night":
            assert obj["visibilityScore"] > 0

    photo_night = body["bestPhotographyNight"]
    day_lookup = visibility_by_day[photo_night["date"]]
    for code in photo_night["bestObjects"]:
        obj = day_lookup[code]
        assert obj["visible"] is True


def test_ranking_helper_filters_and_sorts_visible_objects_only():
    objects = [
        VisibleObjectForecastItem(
            objectCode="MERCURY",
            objectName="Mercury",
            objectType="planet",
            visible=False,
            visibilityScore=0,
            photographyScore=95,
            viewingDirection="W",
            reason="Invisible",
            maxAltitudeDegrees=40,
        ),
        VisibleObjectForecastItem(
            objectCode="MARS",
            objectName="Mars",
            objectType="planet",
            visible=True,
            visibilityScore=0,
            photographyScore=80,
            viewingDirection="E",
            reason="Too low score",
            maxAltitudeDegrees=50,
        ),
        VisibleObjectForecastItem(
            objectCode="JUPITER",
            objectName="Jupiter",
            objectType="planet",
            visible=True,
            visibilityScore=80,
            photographyScore=70,
            viewingDirection="S",
            reason="Good",
            maxAltitudeDegrees=55,
        ),
        VisibleObjectForecastItem(
            objectCode="VENUS",
            objectName="Venus",
            objectType="planet",
            visible=True,
            visibilityScore=80,
            photographyScore=70,
            viewingDirection="S",
            reason="Good",
            maxAltitudeDegrees=45,
        ),
        VisibleObjectForecastItem(
            objectCode="MOON",
            objectName="Moon",
            objectType="moon",
            visible=True,
            visibilityScore=85,
            photographyScore=65,
            viewingDirection="SE",
            reason="Best",
            maxAltitudeDegrees=30,
        ),
    ]

    ranked = get_ranked_visible_objects(objects)
    assert [obj.object_code for obj in ranked] == ["MOON", "JUPITER", "VENUS"]
