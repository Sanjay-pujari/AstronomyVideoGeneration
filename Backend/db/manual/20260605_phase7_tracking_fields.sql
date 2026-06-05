-- Phase 7 minimal tracking fields for astronomy opportunity -> planning -> rendering traceability.
-- Manual PostgreSQL script only; intentionally not an EF migration.

ALTER TABLE astronomy_content_opportunities
    ADD COLUMN IF NOT EXISTS selected_event_object_ids_json jsonb NULL,
    ADD COLUMN IF NOT EXISTS selected_object_names_json jsonb NULL;

ALTER TABLE content_generation_plans
    ADD COLUMN IF NOT EXISTS astronomy_content_opportunity_id uuid NULL,
    ADD COLUMN IF NOT EXISTS astronomy_event_intelligence_id uuid NULL,
    ADD COLUMN IF NOT EXISTS source_event_object_ids_json jsonb NULL,
    ADD COLUMN IF NOT EXISTS planned_object_names_json jsonb NULL,
    ADD COLUMN IF NOT EXISTS plan_status varchar(60) NOT NULL DEFAULT 'Planned',
    ADD COLUMN IF NOT EXISTS planned_format varchar(80) NULL,
    ADD COLUMN IF NOT EXISTS priority_score numeric(5,2) NULL,
    ADD COLUMN IF NOT EXISTS final_video_path text NULL,
    ADD COLUMN IF NOT EXISTS thumbnail_path text NULL,
    ADD COLUMN IF NOT EXISTS failure_reason text NULL,
    ADD COLUMN IF NOT EXISTS completed_utc timestamptz NULL;

ALTER TABLE content_generation_plans
    ALTER COLUMN plan_status SET DEFAULT 'Planned';

DO $$
BEGIN
    IF to_regclass('public.astronomy_content_opportunities') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM pg_constraint
           WHERE conname = 'fk_content_generation_plans_astronomy_content_opportunity_id'
       ) THEN
        ALTER TABLE content_generation_plans
            ADD CONSTRAINT fk_content_generation_plans_astronomy_content_opportunity_id
            FOREIGN KEY (astronomy_content_opportunity_id)
            REFERENCES astronomy_content_opportunities ("Id")
            ON DELETE SET NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.astronomy_event_intelligences') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM pg_constraint
           WHERE conname = 'fk_content_generation_plans_astronomy_event_intelligence_id'
       ) THEN
        ALTER TABLE content_generation_plans
            ADD CONSTRAINT fk_content_generation_plans_astronomy_event_intelligence_id
            FOREIGN KEY (astronomy_event_intelligence_id)
            REFERENCES astronomy_event_intelligences ("Id")
            ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_content_generation_plans_astronomy_content_opportunity_id
    ON content_generation_plans (astronomy_content_opportunity_id);

CREATE INDEX IF NOT EXISTS ix_content_generation_plans_astronomy_event_intelligence_id
    ON content_generation_plans (astronomy_event_intelligence_id);

CREATE INDEX IF NOT EXISTS ix_content_generation_plans_plan_status
    ON content_generation_plans (plan_status);

CREATE INDEX IF NOT EXISTS ix_content_generation_plans_planned_format
    ON content_generation_plans (planned_format);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'astronomy_content_opportunities'
          AND column_name = 'status'
    ) THEN
        CREATE INDEX IF NOT EXISTS ix_astronomy_content_opportunities_status
            ON astronomy_content_opportunities (status);
    ELSIF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'astronomy_content_opportunities'
          AND column_name = 'Status'
    ) THEN
        CREATE INDEX IF NOT EXISTS ix_astronomy_content_opportunities_status
            ON astronomy_content_opportunities ("Status");
    END IF;
END $$;
