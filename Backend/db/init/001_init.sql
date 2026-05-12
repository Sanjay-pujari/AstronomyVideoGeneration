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
    use_topic_planner boolean not null default false,
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

alter table if exists generated_scripts add column if not exists prompt_feedback_context_json text null;


create table if not exists pipeline_stage_executions
(
    id uuid primary key,
    created_utc timestamptz not null,
    updated_utc timestamptz null,
    pipeline_run_id uuid not null,
    stage_name text not null,
    status text not null,
    started_at timestamptz not null,
    finished_at timestamptz null,
    duration_ms bigint null,
    error_message text null,
    metadata_json text null
);

create index if not exists ix_pipeline_stage_executions_run on pipeline_stage_executions(pipeline_run_id, started_at);
create index if not exists ix_pipeline_stage_executions_status on pipeline_stage_executions(status, started_at desc);


alter table if exists published_videos add column if not exists pipeline_run_id uuid null;
create index if not exists ix_published_videos_pipeline_run_id on published_videos(pipeline_run_id, created_at desc);

alter table if exists pipeline_jobs add column if not exists use_topic_planner boolean not null default false;
alter table if exists pipeline_jobs add column if not exists is_stale boolean not null default false;
alter table if exists pipeline_jobs add column if not exists stale_detected_at timestamptz null;
alter table if exists pipeline_jobs add column if not exists recovery_notes text null;
create index if not exists ix_pipeline_jobs_stale on pipeline_jobs(status, is_stale, scheduled_at);

create table if not exists recovery_operations
(
    id uuid primary key,
    created_utc timestamptz not null,
    updated_utc timestamptz null,
    pipeline_run_id uuid null,
    pipeline_job_id uuid null,
    operation_type integer not null,
    requested_at timestamptz not null,
    requested_by text not null,
    status integer not null,
    notes text null,
    result_summary text null
);

create index if not exists ix_recovery_operations_run_requested on recovery_operations(pipeline_run_id, requested_at desc);
create index if not exists ix_recovery_operations_job_requested on recovery_operations(pipeline_job_id, requested_at desc);

create table if not exists monetization_records
(
    id uuid primary key,
    created_utc timestamptz not null,
    updated_utc timestamptz null,
    video_id uuid null,
    youtube_video_id text null,
    content_type integer not null,
    affiliate_links_json text not null,
    link_types_csv text null,
    pinned_comment_text text null,
    created_at timestamptz not null
);

create index if not exists ix_monetization_records_video_created on monetization_records(video_id, created_at desc);

alter table if exists generated_scripts add column if not exists optimized_title text null;
alter table if exists generated_scripts add column if not exists alternate_titles_csv text null;
alter table if exists generated_scripts add column if not exists optimized_description text null;
alter table if exists generated_scripts add column if not exists optimized_tags_csv text null;
alter table if exists generated_scripts add column if not exists optimized_hashtags_csv text null;
alter table if exists generated_scripts add column if not exists thumbnail_text_suggestions_csv text null;
alter table if exists generated_scripts add column if not exists hook_line text null;

alter table if exists published_videos add column if not exists optimized_title text null;
alter table if exists published_videos add column if not exists optimized_description text null;
alter table if exists published_videos add column if not exists optimized_tags_csv text null;
alter table if exists published_videos add column if not exists title_experiment_id uuid null;
alter table if exists published_videos add column if not exists selected_title_variant_id uuid null;
alter table if exists published_videos add column if not exists thumbnail_experiment_id uuid null;
alter table if exists published_videos add column if not exists selected_thumbnail_variant_id uuid null;
alter table if exists published_videos add column if not exists cta_experiment_id uuid null;
alter table if exists published_videos add column if not exists selected_cta_variant_id uuid null;

alter table if exists video_analytics add column if not exists published_video_id uuid null;
alter table if exists video_analytics add column if not exists title_experiment_id uuid null;
alter table if exists video_analytics add column if not exists title_variant_id uuid null;
alter table if exists video_analytics add column if not exists thumbnail_experiment_id uuid null;
alter table if exists video_analytics add column if not exists thumbnail_variant_id uuid null;
alter table if exists video_analytics add column if not exists cta_experiment_id uuid null;
alter table if exists video_analytics add column if not exists cta_variant_id uuid null;
alter table if exists video_analytics add column if not exists "EventId" text null;
alter table if exists video_analytics add column if not exists "EventType" text null;
alter table if exists video_analytics add column if not exists "EventTitle" text null;
create index if not exists ix_video_analytics_published_video on video_analytics(published_video_id, retrieved_at desc);
create index if not exists "IX_video_analytics_EventId" on video_analytics("EventId");

create table if not exists content_experiments
(
    id uuid primary key,
    created_utc timestamptz not null,
    updated_utc timestamptz null,
    video_id uuid not null references published_videos(id),
    experiment_type integer not null,
    selected_variant_id uuid null,
    status integer not null,
    created_at timestamptz not null,
    completed_at timestamptz null
);

create index if not exists ix_content_experiments_video_type_status on content_experiments(video_id, experiment_type, status);

create table if not exists content_variants
(
    id uuid primary key,
    created_utc timestamptz not null,
    updated_utc timestamptz null,
    content_experiment_id uuid not null references content_experiments(id) on delete cascade,
    variant_type integer not null,
    value text not null,
    views bigint not null default 0,
    ctr double precision null,
    engagement_score double precision not null default 0,
    is_winner boolean not null default false
);

create index if not exists ix_content_variants_experiment_winner on content_variants(content_experiment_id, is_winner);


create table if not exists platform_publication_records
(
    id uuid primary key,
    created_utc timestamptz not null,
    updated_utc timestamptz null,
    parent_short_video_id uuid not null references short_videos(id),
    platform integer not null,
    external_post_id text null,
    external_url text null,
    status integer not null,
    published_at timestamptz null,
    error_message text null
);

create index if not exists ix_platform_publication_records_short_platform on platform_publication_records(parent_short_video_id, platform, published_at desc);

-- Keep parity with EF Core model (unique index on (Platform, ExternalPostId)).
create unique index if not exists ix_platform_publication_records_platform_external_post_id
    on platform_publication_records(platform, external_post_id);

alter table if exists platform_content_analytics add column if not exists "PerformanceScore" double precision null;
