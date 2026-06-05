-- Phase 7D.2 Category Registration Audit seed helper.
--
-- Purpose:
--   Safely add any missing Phase 7C/7D astronomy opportunity categories to content_categories.
--
-- Safety notes:
--   * Manual script only; do not wire into application startup or EF migrations.
--   * Inserts missing category codes only.
--   * Existing rows are never updated, including DailySkyGuide and WeeklySkyForecast.
--   * If a category already exists but is inactive, review/activate it manually according to
--     production policy; this script intentionally preserves existing behavior.

WITH required_categories ("Code", "DisplayName", "Priority") AS (
    VALUES
        ('RareEventAlert', 'Rare Event Alert', 100),
        ('PlanetConjunction', 'Planet Conjunction', 100),
        ('PlanetGrouping', 'Planet Grouping', 100),
        ('MoonSpecials', 'Moon Specials', 100),
        ('PlanetVisibilityGuide', 'Planet Visibility Guide', 100),
        ('AstroPhotographyGuide', 'Astro Photography Guide', 100),
        ('AstroExplainer', 'Astro Explainer', 100),
        ('WeeklySkyForecast', 'Weekly Sky Forecast', 100)
), rows_to_insert AS (
    SELECT
        (substr(md5('phase7d2-content-category:' || "Code"), 1, 8) || '-' ||
         substr(md5('phase7d2-content-category:' || "Code"), 9, 4) || '-' ||
         substr(md5('phase7d2-content-category:' || "Code"), 13, 4) || '-' ||
         substr(md5('phase7d2-content-category:' || "Code"), 17, 4) || '-' ||
         substr(md5('phase7d2-content-category:' || "Code"), 21, 12))::uuid AS "Id",
        "Code",
        "DisplayName",
        NULL::text AS "Description",
        TRUE AS "Enabled",
        "Priority",
        TRUE AS "SupportsLongVideo",
        TRUE AS "SupportsShortVideo",
        TRUE AS "SupportsThumbnail",
        TRUE AS "SupportsPublishing",
        TRUE AS "SupportsAiOptimization",
        TIMESTAMPTZ '2026-06-05 00:00:00+00' AS "CreatedUtc",
        TIMESTAMPTZ '2026-06-05 00:00:00+00' AS "UpdatedUtc"
    FROM required_categories
)
INSERT INTO content_categories (
    "Id",
    "Code",
    "DisplayName",
    "Description",
    "Enabled",
    "Priority",
    "SupportsLongVideo",
    "SupportsShortVideo",
    "SupportsThumbnail",
    "SupportsPublishing",
    "SupportsAiOptimization",
    "CreatedUtc",
    "UpdatedUtc"
)
SELECT
    "Id",
    "Code",
    "DisplayName",
    "Description",
    "Enabled",
    "Priority",
    "SupportsLongVideo",
    "SupportsShortVideo",
    "SupportsThumbnail",
    "SupportsPublishing",
    "SupportsAiOptimization",
    "CreatedUtc",
    "UpdatedUtc"
FROM rows_to_insert
ON CONFLICT ("Code") DO NOTHING;

-- Optional verification query for the manual operator:
WITH required_categories ("Code", "DisplayName") AS (
    VALUES
        ('RareEventAlert', 'Rare Event Alert'),
        ('PlanetConjunction', 'Planet Conjunction'),
        ('PlanetGrouping', 'Planet Grouping'),
        ('MoonSpecials', 'Moon Specials'),
        ('PlanetVisibilityGuide', 'Planet Visibility Guide'),
        ('AstroPhotographyGuide', 'Astro Photography Guide'),
        ('AstroExplainer', 'Astro Explainer'),
        ('WeeklySkyForecast', 'Weekly Sky Forecast')
)
SELECT
    required_categories."Code" AS category_code,
    content_categories."Code" IS NOT NULL AS exists,
    COALESCE(content_categories."Enabled", FALSE) AS is_active,
    content_categories."DisplayName" AS display_name,
    (content_categories."Code" IS NOT NULL AND content_categories."Enabled") AS can_plan,
    CASE
        WHEN content_categories."Code" IS NULL THEN 'missing from content_categories'
        WHEN NOT content_categories."Enabled" THEN 'inactive in content_categories'
        ELSE NULL
    END AS warning
FROM required_categories
LEFT JOIN content_categories ON content_categories."Code" = required_categories."Code"
ORDER BY array_position(ARRAY[
    'RareEventAlert',
    'PlanetConjunction',
    'PlanetGrouping',
    'MoonSpecials',
    'PlanetVisibilityGuide',
    'AstroPhotographyGuide',
    'AstroExplainer',
    'WeeklySkyForecast'
], required_categories."Code");
