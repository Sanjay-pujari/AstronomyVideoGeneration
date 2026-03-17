create table if not exists pipeline_runs
(
    id uuid primary key,
    created_utc timestamptz not null,
    updated_utc timestamptz null,
    run_date date not null,
    content_type integer not null,
    location_name text not null,
    time_zone text not null,
    status integer not null,
    failure_reason text null,
    publish_to_youtube boolean not null,
    youtube_video_id text null,
    started_utc timestamptz null,
    finished_utc timestamptz null
);

create table if not exists astronomy_events
(
    id uuid primary key,
    created_utc timestamptz not null,
    event_date date not null,
    category text not null,
    object_name text not null,
    rank_score double precision not null,
    visibility_window text not null,
    direction text not null,
    observation_tool text not null,
    details text not null
);

create table if not exists generated_scripts
(
    id uuid primary key,
    created_utc timestamptz not null,
    pipeline_run_id uuid not null,
    content_type integer not null,
    script_date date not null,
    prompt text not null,
    script_body text not null,
    title text not null,
    description text not null,
    tags_csv text not null,
    estimated_duration_seconds integer not null
);

create table if not exists media_assets
(
    id uuid primary key,
    created_utc timestamptz not null,
    pipeline_run_id uuid not null,
    asset_type text not null,
    file_name text not null,
    local_path text not null,
    blob_path text null,
    public_url text null,
    size_bytes bigint not null
);
