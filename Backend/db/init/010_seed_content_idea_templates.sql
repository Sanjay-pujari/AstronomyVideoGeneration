-- Manual seed script for content_idea_templates.
-- Run this after the content_idea_templates table exists.

INSERT INTO content_idea_templates (
    id,
    content_category_code,
    template_code,
    title_template,
    topic_template,
    description,
    language,
    priority,
    enabled,
    created_utc,
    updated_utc
)
VALUES
(
    '13000000-0000-0000-0000-000000000001',
    'DailySkyGuide',
    'default_daily_en',
    'Tonight in {RegionName}: {ObjectName} Sky Highlights for {Date}',
    'Daily observation plan focused on {ObjectName} and key {EventName} details for {RegionName} in {Language}.',
    'Default English daily guide template',
    'en',
    100,
    TRUE,
    NOW() AT TIME ZONE 'UTC',
    NOW() AT TIME ZONE 'UTC'
),
(
    '13000000-0000-0000-0000-000000000002',
    'RareEventAlert',
    'default_rare_en',
    '{EventName} Alert: Watch {ObjectName} from {RegionName} on {Date}',
    'Urgent rare event briefing for {RegionName}, including visibility, timing, and capture tips in {Language}.',
    'Default English rare event alert template',
    'en',
    100,
    TRUE,
    NOW() AT TIME ZONE 'UTC',
    NOW() AT TIME ZONE 'UTC'
);
