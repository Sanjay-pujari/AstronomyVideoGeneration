from datetime import datetime
from typing import List
from zoneinfo import ZoneInfo

from fastapi import FastAPI
from pydantic import BaseModel
from skyfield import almanac
from skyfield.api import load, wgs84

app = FastAPI(title="Astronomy Skyfield Sidecar")


class DailySkyRequest(BaseModel):
    date: str
    locationName: str
    latitude: float
    longitude: float
    timezone: str


class DailySkyEvent(BaseModel):
    category: str
    objectName: str
    visibilityWindow: str
    direction: str
    observationTool: str
    details: str


class VisualIdea(BaseModel):
    title: str
    description: str


class DailySkyResponse(BaseModel):
    date: str
    locationName: str
    timezone: str
    events: List[DailySkyEvent]
    visualIdeas: List[VisualIdea]


ts = load.timescale()
eph = load("de421.bsp")


def _cardinal_from_azimuth(azimuth: float) -> str:
    points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
    return points[round(azimuth / 45) % 8]


def _moon_phase_name(phase_degrees: float) -> str:
    if phase_degrees < 22.5:
        return "New Moon"
    if phase_degrees < 67.5:
        return "Waxing Crescent"
    if phase_degrees < 112.5:
        return "First Quarter"
    if phase_degrees < 157.5:
        return "Waxing Gibbous"
    if phase_degrees < 202.5:
        return "Full Moon"
    if phase_degrees < 247.5:
        return "Waning Gibbous"
    if phase_degrees < 292.5:
        return "Last Quarter"
    if phase_degrees < 337.5:
        return "Waning Crescent"
    return "New Moon"


def _best_visibility(latitude: float, longitude: float, timezone: str, date: str, body_key: str):
    tz = ZoneInfo(timezone)
    observer = eph["earth"] + wgs84.latlon(latitude_degrees=latitude, longitude_degrees=longitude)
    local_hours = [18, 19, 20, 21, 22, 23]
    times = []

    for hour in local_hours:
        local_time = datetime.fromisoformat(f"{date}T{hour:02d}:00:00").replace(tzinfo=tz)
        utc_time = local_time.astimezone(ZoneInfo("UTC"))
        times.append(ts.utc(utc_time.year, utc_time.month, utc_time.day, utc_time.hour, utc_time.minute))

    altitudes = []
    azimuths = []
    for time_value in times:
        apparent = observer.at(time_value).observe(eph[body_key]).apparent()
        alt, az, _ = apparent.altaz()
        altitudes.append(alt.degrees)
        azimuths.append(az.degrees)

    best_altitude = max(altitudes)
    best_index = altitudes.index(best_altitude)
    return best_altitude, azimuths[best_index], f"{local_hours[0]:02d}:00-{local_hours[-1]:02d}:59"


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/ephemeris/daily-sky", response_model=DailySkyResponse)
def daily_sky(req: DailySkyRequest):
    event_items: List[DailySkyEvent] = []
    ideas: List[VisualIdea] = []

    phase_degrees = almanac.moon_phase(eph, ts.utc(datetime.fromisoformat(f"{req.date}T00:00:00"))).degrees
    moon_phase = _moon_phase_name(phase_degrees)
    moon_altitude, moon_azimuth, moon_window = _best_visibility(req.latitude, req.longitude, req.timezone, req.date, "moon")
    moon_direction = _cardinal_from_azimuth(moon_azimuth)

    event_items.append(
        DailySkyEvent(
            category="Moon",
            objectName=moon_phase,
            visibilityWindow=moon_window,
            direction=moon_direction,
            observationTool="Naked eye / binoculars",
            details=f"Moon phase is {moon_phase}; best altitude around {moon_altitude:.1f}° in the {moon_direction}.",
        )
    )

    for planet_name in ["jupiter barycenter", "venus", "saturn barycenter", "mars"]:
        altitude, azimuth, window = _best_visibility(req.latitude, req.longitude, req.timezone, req.date, planet_name)
        if altitude < 10:
            continue

        display_name = " ".join(word.capitalize() for word in planet_name.replace("barycenter", "").split()).strip()
        direction = _cardinal_from_azimuth(azimuth)
        event_items.append(
            DailySkyEvent(
                category="Planet",
                objectName=display_name,
                visibilityWindow=window,
                direction=direction,
                observationTool="Binoculars / telescope",
                details=f"{display_name} reaches about {altitude:.1f}° altitude in the {direction} during evening hours.",
            )
        )
        break

    event_items.append(
        DailySkyEvent(
            category="Deep Sky",
            objectName="Orion Nebula (placeholder)",
            visibilityWindow="20:00-22:00",
            direction="SW",
            observationTool="Binoculars / small telescope",
            details="Placeholder deep-sky suggestion while detailed constellation calculations are phased in.",
        )
    )

    for item in event_items:
        ideas.append(
            VisualIdea(
                title=f"{item.objectName} in the {item.direction}",
                description=f"Show {item.objectName} position toward the {item.direction} during {item.visibilityWindow}.",
            )
        )

    return DailySkyResponse(
        date=req.date,
        locationName=req.locationName,
        timezone=req.timezone,
        events=event_items,
        visualIdeas=ideas,
    )
