# Orion production content-planning workflow discovery

## Executive summary

The production path for event-backed astronomy content is: source/Skyfield discovery creates or imports `AstronomyEventIntelligence`; event objects are attached as `AstronomyEventObject`; `/api/astronomy-intelligence/generate-content-opportunities` can score a supported transient event into `astronomy_content_opportunities`; `/api/content-planning/create-plan-from-event` creates a draft `content_generation_plans` row from the intelligence row, not from an opportunity; `/api/content-planning/batch-generate-from-plans` selects the plan and, with `useProductionPipeline=true`, calls the Phase 1-20 production pipeline. Orion must not use Skyfield and is not currently representable by the content-opportunity API because that API only maps `PLANET_CONJUNCTION`, `PLANET_GROUPING`, `BRIGHT_PLANET_VISIBILITY`, and `MOON_SPECIAL`, filters to `Status == "Candidate"`, and sets `RequiresSkyfield=true` for all generated opportunities. The exact recommendation is to minimally extend the existing content-opportunity API/service for `CONSTELLATION` evergreen subjects while continuing to use `create-plan-from-event` and batch generation unchanged.

## Database sequence

### `astronomy_events`

- EF entity: `Astronomy.MediaFactory.Core.AstronomyEvent`; DbSet `AstronomyEvents`; mapped with `ToTable("astronomy_events")`; PK `Id`.
- Columns explicitly mapped in `MediaFactoryDbContext`: `eventId`, `eventType`, `title`, `description`, `startUtc`, `peakUtc`, `endUtc`, `targetDate`, `regionId`, `locationName`, `timezone`, `globalVisibility`, `visibilityRegions`, `relatedObjects`, `source`, `status`, `confidenceScore`, `rarityScore`, `visibilityScore`, `audienceInterestScore`, `timingUrgencyScore`, `contentOpportunityScore`, `recommendedContentType`, `createdUtc`, `updatedUtc`.
- Unique index: `EventId`; additional indexes: `TargetDate`, `RegionId`, `EventType`, `ContentOpportunityScore`.
- JSON-like converted columns: `visibilityRegions`, `relatedObjects` are string arrays serialized through EF value conversions, not configured as jsonb here.
- Status column: `status`. Audit columns: `createdUtc`, `updatedUtc`. Family/event-type column: `eventType`. Source external ID column: `eventId`; source column: `source`.
- No FK relationship from this legacy table to `astronomy_event_objects`, `astronomy_content_opportunities`, or `content_generation_plans` was found in the current mapping; the production content-planning workflow uses `astronomy_event_intelligences` as its event parent.

### `astronomy_event_objects`

- EF entity: `Astronomy.MediaFactory.Core.AstronomyEventObject`; DbSet `AstronomyEventObjects`; mapped to `astronomy_event_objects`; PK `Id`.
- FK: `AstronomyEventIntelligenceId` to `AstronomyEventIntelligence`; delete behavior `Cascade`. No FK to `astronomy_events`.
- Required by configuration: `ObjectName` max 160, `ObjectType` max 80, `CreatedUtc`; `AstronomyEventIntelligenceId` is non-nullable by entity type. Nullable: `ObjectRole` max 80, `CatalogId` max 80, `UpdatedUtc`, `Magnitude` precision 5,2, `VisibilityScore` precision 5,2, `MetadataJson` jsonb.
- Indexes: non-unique `ObjectName`, `ObjectType`. No unique index or concurrency token found.
- Audit columns: inherited `CreatedUtc`, `UpdatedUtc`. Object/family columns: `ObjectName`, `ObjectType`, `ObjectRole`. Source/catalog ID column: `CatalogId`. JSON/JSONB: `MetadataJson`.

### `astronomy_content_opportunities`

- EF entity: `Astronomy.MediaFactory.Core.AstronomyContentOpportunity`; DbSet `AstronomyContentOpportunities`; mapped to `astronomy_content_opportunities`; PK `Id`.
- FK: `AstronomyEventIntelligenceId` to `AstronomyEventIntelligence`; delete behavior `Cascade`. No FK to `astronomy_events`, `astronomy_event_objects`, or `content_generation_plans`.
- Required by configuration: `ContentCategory` max 80, `Title` max 240, `Status` max 60, `CreatedUtc`; `AstronomyEventIntelligenceId` and `PriorityScore` are non-nullable entity properties. Nullable: `Angle` max 1000, `AudienceSegment` max 120, `UpdatedUtc`, `SelectedEventObjectIdsJson`, `SelectedObjectNamesJson`, `VisualStrategyJson`, `NarrationStrategyJson`, `MetadataJson`.
- JSON/JSONB columns: `selected_event_object_ids_json`, `selected_object_names_json`, `VisualStrategyJson`, `NarrationStrategyJson`, `MetadataJson`. Status column: `Status`. Indexes: non-unique `ContentCategory`, `PriorityScore`, `Status`; no DB unique index, but service-level duplicate key is `(AstronomyEventIntelligenceId, ContentCategory)`. No concurrency token found.

### `content_generation_plans`

- EF entity: `Astronomy.MediaFactory.Core.ContentGenerationPlan`; DbSet `ContentGenerationPlans`; mapped to `content_generation_plans`; PK `Id`.
- FKs: nullable `astronomy_content_opportunity_id` to `AstronomyContentOpportunity` with `SetNull` on delete; nullable `astronomy_event_intelligence_id` to `AstronomyEventIntelligence` with `SetNull` on delete.
- Explicitly mapped jsonb columns: `requested_output_types_json`, `source_event_object_ids_json`, `planned_object_names_json`, `asset_plan_json`. Status columns: `Status`, `plan_status`, `asset_plan_status` default `Planned`. Audit columns: inherited `CreatedUtc`, `UpdatedUtc`; production completion column `completed_utc`.
- Family/category columns: `ContentCategoryCode`, `PrimaryAstronomyEventTypeCode`, `PrimaryCelestialObjectCode`, `planned_format`. Source external ID column: `source_external_event_id` max 160.
- Indexes: non-unique `ContentCategoryCode`, `PipelineRunId`, `Language`, `RegionId`, `ScheduledUtc`, `Status`, `astronomy_content_opportunity_id`, `astronomy_event_intelligence_id`, `source_external_event_id`, `plan_status`, `planned_format`, `asset_plan_status`. No unique index or concurrency token found.

## Insert and update ownership

- `astronomy_event_objects`: production import path `AstronomyEventVerifiedImportService.ImportVerifiedEventsAsync` calls `ReplaceObjects`, removes existing child rows on overwrite, and adds primary/secondary `AstronomyEventObject` rows from verified JSON. Batch generation can create missing object rows in `ContentPlanBatchGenerationService` when sibling/language plan resolution needs them. Test fixtures and the temporary `OrionContentGenerationPlanSeeder` are not production acceptance paths.
- `astronomy_content_opportunities`: production API path is `POST /api/astronomy-intelligence/generate-content-opportunities` -> `AstronomyContentOpportunityService.GenerateAsync`. It reads candidate `AstronomyEventIntelligences`, filters by region/date/event types, maps supported event families, checks duplicate `(AstronomyEventIntelligenceId, ContentCategory)`, and saves opportunities only when `dryRun=false`. There is no create-one manual opportunity endpoint.
- `content_generation_plans`: `ContentPlanningService.GeneratePlanAsync` writes a direct template-based plan without event intelligence; `ContentPlanningService.CreatePlanFromEventAsync` writes a draft event-backed plan; `AstronomyEventVerifiedImportService.ImportVerifiedEventsAsync` can create draft plans directly while importing verified event JSON; `ContentPlanBatchGenerationService` can create sibling/language plans; `AstronomyVideoPlanningService` and the temporary Orion seeder also insert plans outside this acceptance path. None of these methods opens an explicit EF transaction; each relies on a single `SaveChangesAsync` unit of work, except later production execution which performs multiple saves around pipeline execution.

## Skyfield role

Sidecar endpoints are: `GET /health`; `POST /ephemeris/daily-sky`; `POST /visibility/night-plan`; `POST /forecast/weekly-sky`; `POST /events/yearly-accuracy`; and `POST /ephemeris/weekly-geometry`. The .NET `SkyfieldSidecarClient` calls `/ephemeris/daily-sky`, `/visibility/night-plan`, `/forecast/weekly-sky`, and `/health`; yearly accuracy and weekly geometry exist in Python and supporting models but are not in the interface shown here. Contracts require dated requests, location, latitude, longitude, and timezone for ephemeris/visibility/forecast calls.

The sidecar supports solar-system bodies and a small star/deep-sky catalog (`polaris`, `sirius`, `orion nebula`, `pleiades`, `andromeda galaxy`), plus annual moon phases, planet pairings, meteor moonlight, daily sky, weekly forecast, night-plan visibility, and weekly geometry. It does not model constellations as evergreen editorial subjects. Orion constellation creation should bypass Skyfield; later production phases may still use Stellarium/knowledge/visual intelligence, but the creation of an Orion opportunity should not require sidecar event discovery.

## API map

| Endpoint | Controller/method | DTOs | Reads/writes | Actual behavior for Orion |
|---|---|---|---|---|
| `POST /api/content-planning/generate-plan` | Minimal API in `Program.cs`; `ContentPlanningService.GeneratePlanAsync` | `GenerateContentPlanRequest` -> `GenerateContentPlanResponse` | Reads content categories/templates/styles and optional master celestial object/event type; writes `content_generation_plans` | Can create a direct plan without opportunity or event intelligence; not the requested production opportunity path. `CONSTELLATION` only resolves if a master event type row exists. |
| `POST /api/content-planning/create-plan-from-event` | Minimal API in `Program.cs`; `ContentPlanningService.CreatePlanFromEventAsync` | `CreatePlanFromEventRequest` -> `CreatePlanFromEventResponse` | Reads `astronomy_event_intelligences` with objects; writes `content_generation_plans` | Requires `AstronomyEventIntelligenceId`, matching region/language, `manualValidation=true`, verified or controlled manual-review override, and scheduled time from event. Does not require opportunity ID. Accepts `CONSTELLATION` if a valid intelligence row exists. |
| `POST /api/content-planning/run-category-preparation` | Minimal API; `ManualCategoryPreparationOrchestrator.RunAsync` | `ManualCategoryPreparationRequest` -> `ManualCategoryPreparationResponse` | Creates or reuses planning/preparation artifacts and may create a plan through planning services | Category-based preparation, not an event/opportunity creation API. DTO has category/language/region/regionName/scheduledUtc/primary object and booleans. |
| `POST /api/content-planning/run-category-production-preview` | Minimal API; `CategoryProductionPreview.RunAsync` or category runner | `CategoryProductionPreviewRequest` -> `CategoryProductionPreviewResponse` | Preview execution/artifacts; may generate plan if no plan was supplied in category flow | Preview path, not persistent event/opportunity creation. Requires category/language/region/regionName/scheduledUtc/primary object and publish/diagnostic flags. |
| `GET /api/content-planning/plans-ready-for-generation` | Minimal API; `ContentPlanBatchGenerationService.GetPlansReadyForGenerationAsync` | query `year`, `regionId`, optional `language`, `onlyHighPriority`, `maxPlans` -> `PlansReadyForGenerationResponse` | Reads event-linked plan candidates in year/region/language with runnable statuses | Requires scheduled plan in the requested year and region. Event-backed runnable checks matter, so SQL/direct plans must preserve links and statuses. |
| `POST /api/content-planning/batch-generate-from-plans` | Minimal API; `ContentPlanBatchGenerationService.GenerateFromPlansAsync` | `BatchGenerateFromPlansRequest` -> `BatchGenerateFromPlansResponse` | Reads `content_generation_plans` with intelligence/objects; writes execution rows and plan statuses; with `useProductionPipeline=true` invokes Phase 1-20 | Exact `planId` manual mode can select a plan; production mode requires one plan and calls `ContentPlanProductionExecutionService`, which requires linked `AstronomyEventIntelligence`. |

## Working-family reference

Meteor-shower-like event flow is: (1) source JSON/Skyfield-backed yearly verification prepares verified event payload; (2) `POST /api/astronomy-intelligence/import-verified-events` (service `AstronomyEventVerifiedImportService.ImportVerifiedEventsAsync`) creates/updates `astronomy_event_intelligences`; (3) `ReplaceObjects` inserts `astronomy_event_objects`; (4) `POST /api/astronomy-intelligence/generate-content-opportunities` calls `AstronomyContentOpportunityService.GenerateAsync` for supported candidate families; (5) `astronomy_content_opportunities` rows are saved when not dry-run; (6) `POST /api/content-planning/create-plan-from-event` creates a draft `content_generation_plans` row from the event intelligence; (7) category preparation/preview may be run; (8) `GET /api/content-planning/plans-ready-for-generation` confirms runnable scheduled plans; (9) `POST /api/content-planning/batch-generate-from-plans` with `useProductionPipeline=true` starts Phase 1-20 via `ContentPlanProductionExecutionService` and `ProductionPipelineExecutionService`.

Mandatory for Phase 1-20 production is a runnable `content_generation_plans` row linked to `AstronomyEventIntelligence`, with region/language/schedule/status alignment. Transient-event-specific steps are Skyfield/yearly accuracy, dated event discovery, visibility windows, meteor moonlight, and candidate `StartUtc` filtering. Family extension points are event type normalization, opportunity category mapping, production request mapping, and family semantic/profile resolution.

## Constellation gap analysis

- Can `astronomy_event_objects` represent a constellation? Yes structurally (`ObjectName`, `ObjectType`, `ObjectRole`, `CatalogId`, jsonb metadata) if attached to an `AstronomyEventIntelligence`. Classification: A/B.
- Does an `AstronomyEventIntelligence` have to exist first? Yes for event objects, opportunities, `create-plan-from-event`, and production execution. Classification: D.
- Is event date/time mandatory? `AstronomyEventIntelligence.StartUtc` is non-nullable and ready-for-generation requires plan `ScheduledUtc` in the target year; evergreen content needs an editorial schedule even if not an astronomical peak. Classification: D.
- Is observer location mandatory? Sidecar calls require location/lat/long/timezone; plan APIs require `RegionId` and often `RegionName`; production mapper expects regional context. Orion creation should use region context but not Skyfield-derived observer circumstances. Classification: A/D.
- Is a Skyfield event required? No database FK requires it, and the Orion family is knowledge/evergreen. Current opportunity service incorrectly marks all opportunities `RequiresSkyfield=true`. Classification: C.
- Can opportunity represent evergreen content? Table can; service cannot because it filters `Status == "Candidate"`, applies transient `StartUtc` filters, has no `CONSTELLATION` category map, and lacks a create-one API. Classification: C/D.
- Can `create-plan-from-event` accept `CONSTELLATION`? Yes if a verified, region/language-matching intelligence row exists. Classification: A.
- Does plan generation require transient metadata? Production event-backed execution requires `AstronomyEventIntelligence` and scheduled plan; content generation does not require a row in `astronomy_content_opportunities`. Classification: A/D.
- Are database constraints blocking `CONSTELLATION`? No discovered table constraint blocks the string value, but non-null parent/time fields still require valid data. Classification: B/D.
- Are status workflows compatible? Plan statuses are compatible; opportunity service only processes `Candidate`, while verified import sets `Status="Verified"`, so opportunity generation currently misses verified/manual evergreen rows unless extended. Classification: C.
- Existing manual/non-event path? `generate-plan` can create a direct category plan, but it bypasses the event object/opportunity production workflow and can fail later production because production pipeline requires event intelligence. Classification: D.

## Recommended Orion flow

Proposed sequence after a minimal service/API extension, without implementing it here:

1. Create or import an Orion `AstronomyEventIntelligence` evergreen record through the existing verified-import pipeline or a controlled data-preparation command that produces a verified event-intelligence row with `EventType="CONSTELLATION"`, `RecommendedCategory="AstronomyEducation"`, region/language, editorial `StartUtc`, and Orion event objects. Verification: query `astronomy_event_intelligences` by `external_event_id`/`event_type` and child objects.
2. `POST /api/astronomy-intelligence/generate-content-opportunities` with `AstronomyContentOpportunityRequest` extended/minimally configured to handle `CONSTELLATION` and no Skyfield requirement. Required input: Orion event type/region and optional date bounds that include editorial `StartUtc`. Mutation: one `astronomy_content_opportunities` row with selected Orion object IDs/names and `Status="Proposed"`. Verification: query by `astronomy_event_intelligence_id` and `content_category='AstronomyEducation'`.
3. `POST /api/content-planning/create-plan-from-event` using the `astronomyEventIntelligenceId` from step 1. Mutation: one draft event-backed `content_generation_plans` row; this endpoint does not link the opportunity ID, so the plan is linked by `astronomy_event_intelligence_id` and source event ID. Verification: query plan by intelligence ID, region, language, planned format, and statuses.
4. Optional: `POST /api/content-planning/run-category-preparation` using category/region/language/scheduled time if operational preparation artifacts are desired before production.
5. Optional: `POST /api/content-planning/run-category-production-preview` for preview only.
6. `GET /api/content-planning/plans-ready-for-generation?year=<YEAR>&regionId=<REGION_ID>&language=<LANG>&onlyHighPriority=false&maxPlans=1`; verify Orion is returned or use exact `planId` manual mode.
7. `POST /api/content-planning/batch-generate-from-plans` with `planId=<CONTENT_GENERATION_PLAN_ID>`, `useProductionPipeline=true`, `dryRun=false`, `startPhaseNo=1`, `endPhaseNo=20` when explicitly ready to run production. This discovery task must not run it.

## Draft JSON bodies

No existing endpoint can create an Orion opportunity correctly today. Existing supported DTO bodies are:

```json
{
  "regionId": "<REGION_ID>",
  "startUtc": "<EDITORIAL_WINDOW_START_UTC>",
  "endUtc": "<EDITORIAL_WINDOW_END_UTC>",
  "eventTypes": ["CONSTELLATION"],
  "dryRun": true,
  "maxOpportunities": 1
}
```

```json
{
  "astronomyEventIntelligenceId": "<ASTRONOMY_EVENT_INTELLIGENCE_ID_FROM_PRIOR_STEP>",
  "regionId": "<REGION_ID>",
  "language": "en",
  "plannedFormat": "ShortVideo",
  "requestedOutputs": ["ShortVideo"],
  "manualValidation": true,
  "reason": "Create Orion constellation production plan from verified evergreen constellation intelligence."
}
```

```json
{
  "contentCategoryCode": "AstronomyEducation",
  "language": "en",
  "regionId": "<REGION_ID>",
  "regionName": "<REGION_NAME>",
  "scheduledUtc": "<SCHEDULED_UTC>",
  "primaryCelestialObjectCode": "<ORION_OBJECT_CODE_OR_NULL>",
  "overwriteExisting": false,
  "generatePreviewVideo": true,
  "captureStellariumScenes": true,
  "diagnostics": true
}
```

```json
{
  "contentCategoryCode": "AstronomyEducation",
  "language": "en",
  "regionId": "<REGION_ID>",
  "regionName": "<REGION_NAME>",
  "scheduledUtc": "<SCHEDULED_UTC>",
  "primaryCelestialObjectCode": "<ORION_OBJECT_CODE_OR_NULL>",
  "publishToYouTube": false,
  "publishToFacebook": false,
  "publishToInstagram": false,
  "useAssetAwareVisuals": false,
  "diagnostics": true
}
```

```json
{
  "year": 2026,
  "regionId": "<REGION_ID>",
  "language": "en",
  "maxPlans": 1,
  "onlyHighPriority": false,
  "dryRun": false,
  "useProductionPipeline": true,
  "overwriteExisting": false,
  "startPhaseNo": 1,
  "endPhaseNo": 20,
  "planId": "<CONTENT_GENERATION_PLAN_ID_FROM_CREATE_PLAN_FROM_EVENT>"
}
```

## Verification SQL

```sql
-- 1. Orion event intelligence and event object exist.
select e."Id", e."EventCode", e."ExternalEventId", e."EventType", e."Title", e."RegionId", e."Language", e."VerificationStatus", e."Status", e."StartUtc",
       o."Id" as object_id, o."ObjectName", o."ObjectType", o."ObjectRole", o."CatalogId"
from astronomy_event_intelligences e
left join astronomy_event_objects o on o."AstronomyEventIntelligenceId" = e."Id"
where e."EventType" in ('CONSTELLATION', 'Constellation')
  and (e."Title" ilike '%Orion%' or o."ObjectName" ilike '%Orion%');

-- 2. Orion content opportunity exists.
select co."Id", co."AstronomyEventIntelligenceId", co."ContentCategory", co."Title", co."PriorityScore", co."Status",
       co.selected_event_object_ids_json, co.selected_object_names_json
from astronomy_content_opportunities co
join astronomy_event_intelligences e on e."Id" = co."AstronomyEventIntelligenceId"
where e."EventType" in ('CONSTELLATION', 'Constellation')
  and (co."Title" ilike '%Orion%' or co.selected_object_names_json::text ilike '%Orion%');

-- 3-4. Orion content generation plan exists and links correctly.
select p."Id", p."astronomy_event_intelligence_id", p."astronomy_content_opportunity_id", p."ContentCategoryCode", p."Title", p."RegionId", p."Language",
       p."ScheduledUtc", p."Status", p.plan_status, p.asset_plan_status, p.source_external_event_id, p.planned_format,
       e."ExternalEventId", e."EventType", e."VerificationStatus"
from content_generation_plans p
left join astronomy_event_intelligences e on e."Id" = p."astronomy_event_intelligence_id"
where e."EventType" in ('CONSTELLATION', 'Constellation')
  and (p."Title" ilike '%Orion%' or p.planned_object_names_json::text ilike '%Orion%');

-- 5. Duplicate check.
select e."EventType", e."RegionId", e."Language", p.planned_format, count(*) as active_plan_count
from content_generation_plans p
join astronomy_event_intelligences e on e."Id" = p."astronomy_event_intelligence_id"
where e."EventType" in ('CONSTELLATION', 'Constellation')
  and (p."Title" ilike '%Orion%' or p.planned_object_names_json::text ilike '%Orion%')
  and p.plan_status not in ('Completed','ProductionCompleted','Failed','ProductionFailed','Cancelled','Canceled','Archived')
  and p."Status" not in ('Completed','ProductionCompleted','Failed','ProductionFailed','Cancelled','Canceled','Archived')
group by e."EventType", e."RegionId", e."Language", p.planned_format;

-- 6. Status correctness.
select p."Id", p."Status", p.plan_status, p.asset_plan_status, e."Status" as event_status, e."VerificationStatus", co."Status" as opportunity_status
from content_generation_plans p
join astronomy_event_intelligences e on e."Id" = p."astronomy_event_intelligence_id"
left join astronomy_content_opportunities co on co."AstronomyEventIntelligenceId" = e."Id"
where e."EventType" in ('CONSTELLATION', 'Constellation')
  and (p."Title" ilike '%Orion%' or p.planned_object_names_json::text ilike '%Orion%');

-- 7. Ready-for-generation equivalent predicate for a known year/region/language.
select p."Id", p."Title", p."ScheduledUtc", p."Priority", p."priority_score", p."Status", p.plan_status, e."AutoGenerateAllowed", e."VerificationStatus"
from content_generation_plans p
join astronomy_event_intelligences e on e."Id" = p."astronomy_event_intelligence_id"
where p."RegionId" = '<REGION_ID>'
  and p."Language" = '<LANGUAGE>'
  and p."ScheduledUtc" >= timestamp with time zone '<YEAR>-01-01T00:00:00Z'
  and p."ScheduledUtc" <  timestamp with time zone '<NEXT_YEAR>-01-01T00:00:00Z'
  and p.plan_status not in ('Completed','ProductionCompleted','Failed','ProductionFailed','Cancelled','Canceled','Archived')
  and p."Status" not in ('Completed','ProductionCompleted','Failed','ProductionFailed','Cancelled','Canceled','Archived')
  and e."EventType" in ('CONSTELLATION', 'Constellation')
  and (p."Title" ilike '%Orion%' or p.planned_object_names_json::text ilike '%Orion%');
```

## SQL fallback assessment

Direct SQL could create rows if a parent `astronomy_event_intelligences` row exists, generated GUIDs are supplied, JSONB columns contain selected object IDs/names/strategy metadata, statuses are set to `Proposed` for opportunity and `Draft`/`Planned` as appropriate for plans, audit fields are populated, and duplicate checks mirror service logic. SQL would bypass validation, duplicate/idempotency checks, category/family scoring, requested output normalization, manual-review safeguards, and production request mapping assumptions. Later APIs can consume SQL-created rows only if the event-intelligence link, region/language/schedule/status, selected object JSON, and event type are coherent. Because a small extension can preserve service invariants, direct SQL is not recommended as the production path.

## Required implementation

Small scope. Do not implement in this discovery task. Minimal changes should be limited to `AstronomyContentOpportunityService` and DTO/API handling if needed: add `CONSTELLATION` normalization/category mapping to `AstronomyEducation`, set visual strategy without `RequiresSkyfield`, allow/recognize evergreen verified/manual constellation intelligence rows without forcing transient candidate-only semantics, and add tests for Orion opportunity generation plus unchanged `create-plan-from-event` and batch readiness behavior. If no existing import/source path is acceptable for event-intelligence creation, add a very small create-opportunity/create-evergreen-subject endpoint instead; however, current service extension is less architectural broadening.

## Final decision

MINIMAL API EXTENSION REQUIRED

Scope: Small. The database and downstream plan/pipeline APIs can carry Orion once a verified constellation intelligence row exists, but the existing opportunity-generation API cannot currently create an Orion `astronomy_content_opportunities` row because `CONSTELLATION` is unmapped, all generated opportunities require Skyfield, and evergreen/verified constellation subjects are outside the transient candidate filter.
