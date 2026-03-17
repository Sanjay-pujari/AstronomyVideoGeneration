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


create table if not exists published_videos
(
    id uuid primary key,
    created_utc timestamptz not null,
    title text not null,
    youtube_video_id text null,
    blob_url text null,
    created_at timestamptz not null,
    status text not null
);

create table if not exists short_videos
(
    id uuid primary key,
    created_utc timestamptz not null,
    parent_video_id uuid not null references published_videos(id),
    youtube_video_id text null,
    duration integer not null,
    created_at timestamptz not null
);

create table if not exists pipeline_jobs
(
    id uuid primary key,
    created_utc timestamptz not null,
    updated_utc timestamptz null,
    job_type integer not null,
    parent_pipeline_run_id uuid null,
    status integer not null,
    attempt_count integer not null,
    scheduled_at timestamptz not null,
    started_at timestamptz null,
    finished_at timestamptz null,
    error_message text null,
    run_date date not null,
    content_type integer not null,
    location_name text not null,
    time_zone text not null,
    publish_to_youtube boolean not null,
    next_attempt_at timestamptz null
);

create index if not exists ix_pipeline_jobs_sched_status on pipeline_jobs(status, scheduled_at, next_attempt_at);

create table if not exists video_analytics
(
    id uuid primary key,
    created_utc timestamptz not null,
    updated_utc timestamptz null,
    video_id text not null,
    views bigint not null,
    likes bigint not null,
    comments bigint not null,
    duration_seconds integer not null,
    average_view_duration_seconds double precision null,
    ctr_percent double precision null,
    retrieved_at timestamptz not null,
    content_type integer not null,
    is_short boolean not null,
    parent_video_id text null,
    title text null,
    hook_line text null
);

create index if not exists ix_video_analytics_video_id on video_analytics(video_id, retrieved_at desc);

alter table if exists published_videos add column if not exists thumbnail_path text null;
alter table if exists published_videos add column if not exists thumbnail_url text null;
alter table if exists published_videos add column if not exists thumbnail_uploaded_to_youtube boolean not null default false;
