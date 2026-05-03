from datetime import datetime, timedelta
from typing import Annotated
from zoneinfo import ZoneInfo

from fastapi import FastAPI
from pydantic import BaseModel, ConfigDict, Field, field_validator
from skyfield import almanac
from skyfield.api import Star, load, wgs84

app = FastAPI(title="Astronomy Skyfield Sidecar")
ts = load.timescale()
eph = load("de421.bsp")

STAR_CATALOG = {
    "polaris": (2.530301028, 89.264109444),
    "sirius": (6.752481, -16.716116),
    "orion nebula": (5.588138889, -5.391111111),
    "pleiades": (3.79, 24.1167),
    "andromeda galaxy": (0.712, 41.269)
}
PLANET_KEYS = {
    "mercury": "mercury",
    "venus": "venus",
    "mars": "mars",
    "jupiter": "jupiter barycenter",
    "saturn": "saturn barycenter",
    "uranus": "uranus barycenter",
    "neptune": "neptune barycenter",
    "moon": "moon",
}

OBJECT_MAP = {
    "Moon": "MOON",
    "Mercury": "MERCURY",
    "Venus": "VENUS",
    "Earth": "EARTH",
    "Mars": "MARS",
    "Jupiter": "JUPITER BARYCENTER",
    "Saturn": "SATURN BARYCENTER",
    "Uranus": "URANUS BARYCENTER",
    "Neptune": "NEPTUNE BARYCENTER",
    "Pluto": "PLUTO BARYCENTER",
}

class VisibilityCandidate(BaseModel):
    object_name: Annotated[str, Field(alias="objectName")]
    object_type: Annotated[str, Field(alias="objectType")]
    model_config = ConfigDict(populate_by_name=True, str_strip_whitespace=True)



class DailySkyRequest(BaseModel):
    date: str
    location_name: Annotated[str, Field(alias="locationName", min_length=2)]
    latitude: Annotated[float, Field(ge=-90, le=90)]
    longitude: Annotated[float, Field(ge=-180, le=180)]
    timezone: Annotated[str, Field(min_length=1)]
    model_config = ConfigDict(populate_by_name=True, str_strip_whitespace=True)

    @field_validator("date")
    @classmethod
    def validate_date(cls, v: str) -> str:
        datetime.strptime(v, "%Y-%m-%d")
        return v


class DailySkyEvent(BaseModel):
    category: str
    object_name: Annotated[str, Field(alias="objectName")]
    visibility_window: Annotated[str, Field(alias="visibilityWindow")]
    direction: str
    observation_tool: Annotated[str, Field(alias="observationTool")]
    details: str
    model_config = ConfigDict(populate_by_name=True)


class VisualIdea(BaseModel):
    title: str
    description: str


class DailySkyResponse(BaseModel):
    date: str
    location_name: Annotated[str, Field(alias="locationName")]
    timezone: str
    events: list[DailySkyEvent]
    visual_ideas: Annotated[list[VisualIdea], Field(alias="visualIdeas")]
    model_config = ConfigDict(populate_by_name=True)

class NightPlanRequest(BaseModel):
    date: str
    location_name: Annotated[str, Field(alias="locationName", min_length=2)]
    latitude: Annotated[float, Field(ge=-90, le=90)]
    longitude: Annotated[float, Field(ge=-180, le=180)]
    timezone: Annotated[str, Field(min_length=1)]
    night_window_start_utc: Annotated[str | None, Field(alias="nightWindowStartUtc")] = None
    night_window_end_utc: Annotated[str | None, Field(alias="nightWindowEndUtc")] = None
    minimum_altitude_degrees: Annotated[float, Field(alias="minimumAltitudeDegrees", ge=-30, le=90)] = 10
    step_minutes: Annotated[int, Field(alias="stepMinutes", ge=5, le=120)] = 15
    candidates: list[VisibilityCandidate] = []
    model_config = ConfigDict(populate_by_name=True, str_strip_whitespace=True)
    @field_validator("date")
    @classmethod
    def validate_date(cls, v: str) -> str:
        datetime.strptime(v, "%Y-%m-%d")
        return v

class VisibilitySample(BaseModel):
    local_time: Annotated[str, Field(alias="localTime")]
    utc_time: Annotated[str, Field(alias="utcTime")]
    altitude_degrees: Annotated[float, Field(alias="altitudeDegrees")]
    azimuth_degrees: Annotated[float, Field(alias="azimuthDegrees")]
    direction_label: Annotated[str, Field(alias="directionLabel")]
    is_visible_candidate: Annotated[bool, Field(alias="isVisibleCandidate")]
    model_config = ConfigDict(populate_by_name=True)

class ObjectVisibility(BaseModel):
    object_name: Annotated[str, Field(alias="objectName")]
    object_type: Annotated[str, Field(alias="objectType")]
    is_visible: Annotated[bool, Field(alias="isVisible")]
    visibility_reason: Annotated[str, Field(alias="visibilityReason")]
    samples: list[VisibilitySample]
    best_local_time: Annotated[str|None, Field(alias="bestLocalTime")] = None
    best_utc_time: Annotated[str|None, Field(alias="bestUtcTime")] = None
    altitude_degrees: Annotated[float|None, Field(alias="altitudeDegrees")] = None
    azimuth_degrees: Annotated[float|None, Field(alias="azimuthDegrees")] = None
    direction_label: Annotated[str|None, Field(alias="directionLabel")] = None
    model_config = ConfigDict(populate_by_name=True)

class NightPlanResponse(BaseModel):
    location_name: Annotated[str, Field(alias="locationName")]
    timezone: str
    target_date: Annotated[str, Field(alias="targetDate")]
    sunset_local: Annotated[str, Field(alias="sunsetLocal")]
    sunrise_local: Annotated[str, Field(alias="sunriseLocal")]
    night_window_start_utc: Annotated[str, Field(alias="nightWindowStartUtc")]
    night_window_end_utc: Annotated[str, Field(alias="nightWindowEndUtc")]
    visible_objects: Annotated[list[ObjectVisibility], Field(alias="visibleObjects")]
    not_visible_objects: Annotated[list[ObjectVisibility], Field(alias="notVisibleObjects")]
    model_config = ConfigDict(populate_by_name=True)

def _cardinal(az: float) -> str:
    return ["N","NE","E","SE","S","SW","W","NW"][round(az / 45) % 8]

def _resolve_target(name: str, obj_type: str):
    key = name.strip().lower()
    if key in PLANET_KEYS:
        return ("planet", PLANET_KEYS[key])
    if obj_type.lower() in ("moon","planet") and key in PLANET_KEYS:
        return ("planet", PLANET_KEYS[key])
    if key in STAR_CATALOG:
        ra_h, dec_d = STAR_CATALOG[key]
        return ("star", Star(ra_hours=ra_h, dec_degrees=dec_d))
    return (None, None)

@app.post('/visibility/night-plan', response_model=NightPlanResponse)
def night_plan(req: NightPlanRequest):
    tz = ZoneInfo(req.timezone)
    d = datetime.strptime(req.date, '%Y-%m-%d').date()
    t0_local = datetime.combine(d, datetime.min.time()).replace(tzinfo=tz)
    t1_local = t0_local + timedelta(days=1)
    observer = eph['earth'] + wgs84.latlon(latitude_degrees=req.latitude, longitude_degrees=req.longitude)
    f = almanac.sunrise_sunset(eph, wgs84.latlon(req.latitude, req.longitude))
    t0 = ts.from_datetime(t0_local.astimezone(ZoneInfo('UTC')))
    t1 = ts.from_datetime(t1_local.astimezone(ZoneInfo('UTC')))
    times, events = almanac.find_discrete(t0, t1, f)
    sunset_local, sunrise_local = t0_local.replace(hour=18, minute=30), (t0_local+timedelta(days=1)).replace(hour=6, minute=0)
    for t,e in zip(times, events):
        local = t.utc_datetime().replace(tzinfo=ZoneInfo('UTC')).astimezone(tz)
        if e == 0: sunset_local = local
        if e == 1 and local > sunset_local: sunrise_local = local
    visible, not_visible = [], []
    if req.night_window_start_utc and req.night_window_end_utc:
        window_start_utc = datetime.fromisoformat(req.night_window_start_utc.replace("Z", "+00:00")).astimezone(ZoneInfo("UTC"))
        window_end_utc = datetime.fromisoformat(req.night_window_end_utc.replace("Z", "+00:00")).astimezone(ZoneInfo("UTC"))
    else:
        window_start_utc = sunset_local.astimezone(ZoneInfo("UTC"))
        window_end_utc = sunrise_local.astimezone(ZoneInfo("UTC"))
    if not req.candidates:
        req.candidates = [
            VisibilityCandidate(objectName="Moon", objectType="moon"),
            VisibilityCandidate(objectName="Jupiter", objectType="planet"),
            VisibilityCandidate(objectName="Venus", objectType="planet"),
            VisibilityCandidate(objectName="Saturn", objectType="planet"),
        ]

    print(f"[Skyfield] Request: {req}")

    for c in req.candidates:
        print(f"[Skyfield] Processing {c.object_name}")
        kind, target = _resolve_target(c.object_name, c.object_type)
        samples=[]
        if not target:
            ov=ObjectVisibility(objectName=c.object_name, objectType=c.object_type, isVisible=False, visibilityReason='Object not in supported catalog/ephemeris.', samples=[])
            not_visible.append(ov); continue

        body = None
        if kind == "planet":
            mapped_target = OBJECT_MAP.get(c.object_name, c.object_name.upper())
            print(f"[Skyfield] Object mapping: object_name={c.object_name}, mapped_target={mapped_target}")
            try:
                body = eph[mapped_target]
            except KeyError as ex:
                print(f"[Skyfield ERROR] Missing ephemeris target for object_name={c.object_name}, mapped_target={mapped_target}: {str(ex)}")
                ov=ObjectVisibility(objectName=c.object_name, objectType=c.object_type, isVisible=False, visibilityReason='Object not in loaded ephemeris kernel.', samples=[])
                not_visible.append(ov)
                continue
        else:
            body = target

        best=None
        t_utc=window_start_utc
        while t_utc<=window_end_utc:
            t=t_utc.astimezone(tz)
            ts_t=ts.from_datetime(t_utc)
            apparent = observer.at(ts_t).observe(body).apparent()
            alt,az,_=apparent.altaz()
            a=float(alt.degrees); z=float(az.degrees)
            s=VisibilitySample(localTime=t.isoformat(), utcTime=t_utc.isoformat(), altitudeDegrees=round(a,2), azimuthDegrees=round(z,2), directionLabel=_cardinal(z), isVisibleCandidate=a>=req.minimum_altitude_degrees)
            samples.append(s)
            if s.is_visible_candidate and (best is None or s.altitude_degrees>best.altitude_degrees): best=s
            t_utc += timedelta(minutes=req.step_minutes)
        if best:
            ov=ObjectVisibility(objectName=c.object_name, objectType=c.object_type, isVisible=True, visibilityReason='Highest altitude above threshold during night window', samples=samples, bestLocalTime=best.local_time, bestUtcTime=best.utc_time, altitudeDegrees=best.altitude_degrees, azimuthDegrees=best.azimuth_degrees, directionLabel=best.direction_label)
            visible.append(ov)
        else:
            ov=ObjectVisibility(objectName=c.object_name, objectType=c.object_type, isVisible=False, visibilityReason='Below minimum altitude during night window', samples=samples)
            not_visible.append(ov)

    print(f"[Skyfield] Visible objects: {len(visible)}")
    return NightPlanResponse(locationName=req.location_name, timezone=req.timezone, targetDate=req.date, sunsetLocal=sunset_local.isoformat(), sunriseLocal=sunrise_local.isoformat(), nightWindowStartUtc=window_start_utc.isoformat().replace("+00:00","Z"), nightWindowEndUtc=window_end_utc.isoformat().replace("+00:00","Z"), visibleObjects=visible or [], notVisibleObjects=not_visible or [])


@app.post('/ephemeris/daily-sky', response_model=DailySkyResponse)
def daily_sky(req: DailySkyRequest):
    return DailySkyResponse(
        date=req.date,
        locationName=req.location_name,
        timezone=req.timezone,
        events=[
            DailySkyEvent(
                category='planet',
                objectName='Venus',
                visibilityWindow='After sunset',
                direction='W',
                observationTool='Naked eye',
                details='Bright evening target low in the western sky.'
            ),
            DailySkyEvent(
                category='moon',
                objectName='Moon',
                visibilityWindow='Evening to early night',
                direction='SE',
                observationTool='Naked eye / binoculars',
                details='Good for a foreground landscape composition.'
            )
        ],
        visualIdeas=[
            VisualIdea(
                title='Golden-hour moonrise timelapse',
                description='Frame terrestrial foreground elements and capture moonrise transitions into blue hour.'
            ),
            VisualIdea(
                title='Venus over skyline',
                description='Shoot short telephoto clips right after sunset while Venus is still bright against twilight.'
            )
        ]
    )
