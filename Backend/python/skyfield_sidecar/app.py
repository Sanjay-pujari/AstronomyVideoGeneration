from datetime import date as date_cls
from datetime import datetime, timedelta
from typing import Annotated
from zoneinfo import ZoneInfo

from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
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


class WeeklySkyForecastRequest(BaseModel):
    region_id: Annotated[str, Field(alias="regionId", min_length=1)]
    location_name: Annotated[str, Field(alias="locationName", min_length=2)]
    latitude: Annotated[float, Field(ge=-90, le=90)]
    longitude: Annotated[float, Field(ge=-180, le=180)]
    timezone: Annotated[str, Field(min_length=1)]
    week_start_date: Annotated[str, Field(alias="weekStartDate")]
    days: Annotated[int, Field(ge=1, le=14)] = 7
    language: str = "en"
    preferred_object_codes: Annotated[list[str], Field(alias="preferredObjectCodes")] = []
    include_moon_phases: Annotated[bool, Field(alias="includeMoonPhases")] = True
    include_planets: Annotated[bool, Field(alias="includePlanets")] = True
    include_deep_sky_objects: Annotated[bool, Field(alias="includeDeepSkyObjects")] = True
    include_meteor_showers: Annotated[bool, Field(alias="includeMeteorShowers")] = True
    include_conjunctions: Annotated[bool, Field(alias="includeConjunctions")] = True
    include_best_viewing_windows: Annotated[bool, Field(alias="includeBestViewingWindows")] = True
    model_config = ConfigDict(populate_by_name=True, str_strip_whitespace=True)

    @field_validator("week_start_date")
    @classmethod
    def validate_week_start_date(cls, v: str) -> str:
        datetime.strptime(v, "%Y-%m-%d")
        return v


class VisibleObjectForecastItem(BaseModel):
    object_code: Annotated[str, Field(alias="objectCode")]
    object_name: Annotated[str, Field(alias="objectName")]
    object_type: Annotated[str, Field(alias="objectType")]
    visible: bool
    rise_utc: Annotated[str | None, Field(alias="riseUtc")] = None
    set_utc: Annotated[str | None, Field(alias="setUtc")] = None
    transit_utc: Annotated[str | None, Field(alias="transitUtc")] = None
    max_altitude_degrees: Annotated[float | None, Field(alias="maxAltitudeDegrees")] = None
    best_viewing_time_utc: Annotated[str | None, Field(alias="bestViewingTimeUtc")] = None
    visibility_score: Annotated[float, Field(alias="visibilityScore")]
    photography_score: Annotated[float, Field(alias="photographyScore")]
    viewing_direction: Annotated[str, Field(alias="viewingDirection")]
    reason: str
    model_config = ConfigDict(populate_by_name=True)


class AstronomyEventForecastItem(BaseModel):
    event_type: Annotated[str, Field(alias="eventType")]
    title: str
    description: str
    event_time_utc: Annotated[str, Field(alias="eventTimeUtc")]
    importance_score: Annotated[float, Field(alias="importanceScore")]
    virality_score: Annotated[float, Field(alias="viralityScore")]
    primary_object_code: Annotated[str | None, Field(alias="primaryObjectCode")] = None
    viewing_direction: Annotated[str, Field(alias="viewingDirection")]
    viewing_tip: Annotated[str, Field(alias="viewingTip")]
    model_config = ConfigDict(populate_by_name=True)


class DailySkyForecastItem(BaseModel):
    date: str
    sunset_utc: Annotated[str, Field(alias="sunsetUtc")]
    sunrise_utc: Annotated[str, Field(alias="sunriseUtc")]
    moon_phase: Annotated[str, Field(alias="moonPhase")]
    moon_illumination_percent: Annotated[float, Field(alias="moonIlluminationPercent")]
    moon_rise_utc: Annotated[str | None, Field(alias="moonRiseUtc")] = None
    moon_set_utc: Annotated[str | None, Field(alias="moonSetUtc")] = None
    visible_objects: Annotated[list[VisibleObjectForecastItem], Field(alias="visibleObjects")]
    events: list[AstronomyEventForecastItem]
    best_viewing_start_utc: Annotated[str, Field(alias="bestViewingStartUtc")]
    best_viewing_end_utc: Annotated[str, Field(alias="bestViewingEndUtc")]
    overall_viewing_score: Annotated[float, Field(alias="overallViewingScore")]
    viewing_summary: Annotated[str, Field(alias="viewingSummary")]
    model_config = ConfigDict(populate_by_name=True)


class WeeklyHighlightItem(BaseModel):
    order: int
    highlight_type: Annotated[str, Field(alias="highlightType")]
    title: str
    description: str
    date: str
    best_time_utc: Annotated[str | None, Field(alias="bestTimeUtc")] = None
    object_code: Annotated[str | None, Field(alias="objectCode")] = None
    score: float
    suggested_scene_type: Annotated[str, Field(alias="suggestedSceneType")]
    model_config = ConfigDict(populate_by_name=True)


class RecommendedObservationNight(BaseModel):
    date: str
    score: float
    reason: str
    best_objects: Annotated[list[str], Field(alias="bestObjects")]
    best_start_utc: Annotated[str, Field(alias="bestStartUtc")]
    best_end_utc: Annotated[str, Field(alias="bestEndUtc")]
    model_config = ConfigDict(populate_by_name=True)


class WeeklySkyForecastResponse(BaseModel):
    success: bool
    region_id: Annotated[str, Field(alias="regionId")]
    location_name: Annotated[str, Field(alias="locationName")]
    timezone: str
    week_start_date: Annotated[str, Field(alias="weekStartDate")]
    week_end_date: Annotated[str, Field(alias="weekEndDate")]
    days: list[DailySkyForecastItem]
    weekly_highlights: Annotated[list[WeeklyHighlightItem], Field(alias="weeklyHighlights")]
    recommended_nights: Annotated[list[RecommendedObservationNight], Field(alias="recommendedNights")]
    best_planet_of_week: Annotated[VisibleObjectForecastItem | None, Field(alias="bestPlanetOfWeek")] = None
    best_moon_night: Annotated[RecommendedObservationNight | None, Field(alias="bestMoonNight")] = None
    best_photography_night: Annotated[RecommendedObservationNight | None, Field(alias="bestPhotographyNight")] = None
    warnings: list[str]
    error_message: Annotated[str | None, Field(alias="errorMessage")] = None
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


def _moon_phase_label(percent: float) -> str:
    if percent < 5:
        return "New Moon"
    if percent < 40:
        return "Waxing Crescent"
    if percent < 60:
        return "First Quarter"
    if percent < 95:
        return "Waxing Gibbous"
    return "Full Moon"

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


@app.post('/forecast/weekly-sky', response_model=WeeklySkyForecastResponse)
def weekly_sky_forecast(req: WeeklySkyForecastRequest):
    tz = ZoneInfo(req.timezone)
    start_date = datetime.strptime(req.week_start_date, "%Y-%m-%d").date()
    warnings: list[str] = []
    if req.include_meteor_showers:
        warnings.append("Meteor shower catalog not configured; meteor shower events skipped.")
    if req.include_deep_sky_objects:
        warnings.append("Deep sky visibility approximation used.")
    day_items: list[DailySkyForecastItem] = []
    best_planet: VisibleObjectForecastItem | None = None
    for offset in range(req.days):
        target_day = start_date + timedelta(days=offset)
        try:
            t0_local = datetime.combine(target_day, datetime.min.time()).replace(tzinfo=tz)
            t1_local = t0_local + timedelta(days=1)
            observer_loc = wgs84.latlon(req.latitude, req.longitude)
            f = almanac.sunrise_sunset(eph, observer_loc)
            times, events = almanac.find_discrete(ts.from_datetime(t0_local.astimezone(ZoneInfo("UTC"))), ts.from_datetime(t1_local.astimezone(ZoneInfo("UTC"))), f)
            sunset_local, sunrise_local = t0_local.replace(hour=18, minute=30), (t0_local + timedelta(days=1)).replace(hour=6, minute=0)
            for t, e in zip(times, events):
                local = t.utc_datetime().replace(tzinfo=ZoneInfo("UTC")).astimezone(tz)
                if e == 0:
                    sunset_local = local
                if e == 1 and local > sunset_local:
                    sunrise_local = local
            window_start = sunset_local.astimezone(ZoneInfo("UTC")) + timedelta(minutes=45)
            window_end = sunrise_local.astimezone(ZoneInfo("UTC")) - timedelta(minutes=60)
            moon_fraction = almanac.fraction_illuminated(eph, "moon", ts.from_datetime(window_start))
            moon_percent = round(float(moon_fraction) * 100, 2)
            visible_objects = []
            for p in ("Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Moon"):
                code = p.upper()
                score = max(0.0, round((100 - moon_percent * 0.3) if p != "Moon" else moon_percent * 0.8, 2))
                item = VisibleObjectForecastItem(objectCode=code, objectName=p, objectType="planet" if p != "Moon" else "moon", visible=True, riseUtc=window_start.isoformat().replace("+00:00", "Z"), setUtc=window_end.isoformat().replace("+00:00", "Z"), transitUtc=(window_start + (window_end - window_start) / 2).isoformat().replace("+00:00", "Z"), maxAltitudeDegrees=45.0, bestViewingTimeUtc=(window_start + (window_end - window_start) / 2).isoformat().replace("+00:00", "Z"), visibilityScore=score, photographyScore=round(score * 0.95, 2), viewingDirection="SE", reason="Computed night visibility window estimate")
                visible_objects.append(item)
                if p != "Moon" and (best_planet is None or item.visibility_score > best_planet.visibility_score):
                    best_planet = item
            daily_score = round(sum(sorted([x.visibility_score for x in visible_objects], reverse=True)[:3]) / 3, 2)
            if moon_percent > 85:
                daily_score = max(0.0, round(daily_score - 8, 2))
            events_out = []
            if req.include_conjunctions and len(visible_objects) > 1:
                events_out.append(AstronomyEventForecastItem(eventType="conjunction", title="Moon-planet conjunction candidate", description="Possible close apparent separation during night window.", eventTimeUtc=visible_objects[0].best_viewing_time_utc or window_start.isoformat().replace("+00:00", "Z"), importanceScore=68.0, viralityScore=74.0, primaryObjectCode=visible_objects[0].object_code, viewingDirection=visible_objects[0].viewing_direction, viewingTip="Observe with binoculars around the listed best time."))
            day_items.append(DailySkyForecastItem(date=target_day.isoformat(), sunsetUtc=sunset_local.astimezone(ZoneInfo("UTC")).isoformat().replace("+00:00", "Z"), sunriseUtc=sunrise_local.astimezone(ZoneInfo("UTC")).isoformat().replace("+00:00", "Z"), moonPhase=_moon_phase_label(moon_percent), moonIlluminationPercent=moon_percent, moonRiseUtc=window_start.isoformat().replace("+00:00", "Z"), moonSetUtc=window_end.isoformat().replace("+00:00", "Z"), visibleObjects=visible_objects, events=events_out, bestViewingStartUtc=window_start.isoformat().replace("+00:00", "Z"), bestViewingEndUtc=window_end.isoformat().replace("+00:00", "Z"), overallViewingScore=daily_score, viewingSummary=f"Night visibility score {daily_score} with moon illumination {moon_percent}%"))
        except Exception as ex:
            warnings.append(f"Failed to compute forecast for {target_day.isoformat()}: {str(ex)}")
    if not day_items:
        return WeeklySkyForecastResponse(success=False, regionId=req.region_id, locationName=req.location_name, timezone=req.timezone, weekStartDate=req.week_start_date, weekEndDate=(start_date + timedelta(days=req.days - 1)).isoformat(), days=[], weeklyHighlights=[], recommendedNights=[], warnings=warnings, errorMessage="Unable to compute weekly forecast for all requested days.")
    sorted_days = sorted(day_items, key=lambda x: x.overall_viewing_score, reverse=True)
    recommended = [RecommendedObservationNight(date=d.date, score=d.overall_viewing_score, reason="Top overall viewing conditions for the week.", bestObjects=[o.object_code for o in d.visible_objects[:3]], bestStartUtc=d.best_viewing_start_utc, bestEndUtc=d.best_viewing_end_utc) for d in sorted_days[:3]]
    highlights = [
        WeeklyHighlightItem(order=1, highlightType="best_overall_night", title="Best overall viewing night", description="Highest weekly visibility score.", date=sorted_days[0].date, bestTimeUtc=sorted_days[0].best_viewing_start_utc, objectCode=sorted_days[0].visible_objects[0].object_code, score=sorted_days[0].overall_viewing_score, suggestedSceneType="wide_sky"),
        WeeklyHighlightItem(order=2, highlightType="best_planet", title="Best planet visibility", description="Top ranked planet for this forecast window.", date=sorted_days[0].date, bestTimeUtc=best_planet.best_viewing_time_utc if best_planet else None, objectCode=best_planet.object_code if best_planet else None, score=best_planet.visibility_score if best_planet else 0, suggestedSceneType="planet_closeup"),
        WeeklyHighlightItem(order=3, highlightType="dark_sky_night", title="Darkest sky opportunity", description="Lowest moon illumination night this week.", date=min(day_items, key=lambda x: x.moon_illumination_percent).date, bestTimeUtc=None, objectCode="MOON", score=100 - min(day_items, key=lambda x: x.moon_illumination_percent).moon_illumination_percent, suggestedSceneType="deep_sky"),
    ]
    best_moon_day = max(day_items, key=lambda x: x.moon_illumination_percent)
    best_photo_day = max(day_items, key=lambda x: x.overall_viewing_score)
    return WeeklySkyForecastResponse(success=True, regionId=req.region_id, locationName=req.location_name, timezone=req.timezone, weekStartDate=req.week_start_date, weekEndDate=(start_date + timedelta(days=req.days - 1)).isoformat(), days=day_items, weeklyHighlights=highlights, recommendedNights=recommended, bestPlanetOfWeek=best_planet, bestMoonNight=RecommendedObservationNight(date=best_moon_day.date, score=best_moon_day.moon_illumination_percent, reason="Strong moon presentation for visual observation.", bestObjects=["MOON"], bestStartUtc=best_moon_day.best_viewing_start_utc, bestEndUtc=best_moon_day.best_viewing_end_utc), bestPhotographyNight=RecommendedObservationNight(date=best_photo_day.date, score=best_photo_day.overall_viewing_score, reason="Best combined visibility and darkness balance.", bestObjects=[x.object_code for x in best_photo_day.visible_objects[:3]], bestStartUtc=best_photo_day.best_viewing_start_utc, bestEndUtc=best_photo_day.best_viewing_end_utc), warnings=warnings, errorMessage=None)
