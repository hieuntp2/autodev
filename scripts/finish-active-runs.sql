-- Mark active AutoDevRunner runs as finished.
--
-- Defaults:
--   - Pending/Running runs become Paused.
--   - Project.CurrentTask is preserved.
--   - Project LastRunStatus/LastRunAt/LastError are updated for affected projects.
--
-- Usage:
--   psql -h localhost -p 5432 -U postgres -d autodev -f scripts/finish-active-runs.sql
--
-- Preview without committing:
--   psql -h localhost -p 5432 -U postgres -d autodev -v dry_run=true -f scripts/finish-active-runs.sql
--
-- Mark as another terminal status:
--   psql -h localhost -p 5432 -U postgres -d autodev -v target_status=Failed -f scripts/finish-active-runs.sql
--   psql -h localhost -p 5432 -U postgres -d autodev -v target_status=Success -f scripts/finish-active-runs.sql
--
-- Clear resumed task text for affected projects:
--   psql -h localhost -p 5432 -U postgres -d autodev -v clear_current_task=true -f scripts/finish-active-runs.sql
--
-- Note: this updates DB state only. If a target repo still has
-- .ai-runner/run.lock, remove that stale lock separately after confirming the
-- runner process is not actually active.

\set ON_ERROR_STOP on

\if :{?target_status}
\else
\set target_status 'Paused'
\endif

\if :{?reason}
\else
\set reason 'Manually finished active AutoDev run from SQL maintenance script.'
\endif

\if :{?clear_current_task}
\else
\set clear_current_task false
\endif

\if :{?dry_run}
\else
\set dry_run false
\endif

BEGIN;

CREATE TEMP TABLE _autodev_finish_options ON COMMIT DROP AS
SELECT
    :'target_status'::text AS target_status,
    :'reason'::text AS reason,
    :'clear_current_task'::boolean AS clear_current_task;

DO $$
DECLARE
    opts record;
BEGIN
    SELECT * INTO opts FROM _autodev_finish_options;

    IF opts.target_status NOT IN ('Success', 'Paused', 'Failed', 'QuotaLimit', 'AuthError') THEN
        RAISE EXCEPTION
            'target_status must be one of Success, Paused, Failed, QuotaLimit, AuthError. Got: %',
            opts.target_status;
    END IF;
END $$;

CREATE TEMP TABLE _autodev_finished_runs (
    "Id" integer,
    "ProjectId" integer,
    "OldStatus" text,
    "NewStatus" text,
    "TaskTitle" text
) ON COMMIT DROP;

WITH active AS (
    SELECT r."Id", r."Status" AS "OldStatus"
    FROM "Runs" AS r
    WHERE r."Status" IN ('Pending', 'Running')
),
updated AS (
    UPDATE "Runs" AS r
    SET
        "Status" = opts.target_status,
        "FinishedAt" = COALESCE(r."FinishedAt", CURRENT_TIMESTAMP),
        "Reason" = COALESCE(NULLIF(r."Reason", ''), opts.reason),
        "Stage" = CASE
            WHEN opts.target_status = 'Failed' THEN 'Failed'
            WHEN r."Stage" IS NULL OR r."Stage" IN ('Planned', 'Running') THEN 'Reported'
            ELSE r."Stage"
        END
    FROM _autodev_finish_options AS opts, active
    WHERE r."Id" = active."Id"
    RETURNING
        r."Id",
        r."ProjectId",
        active."OldStatus",
        r."Status" AS "NewStatus",
        r."TaskTitle"
)
INSERT INTO _autodev_finished_runs
SELECT * FROM updated;

CREATE TEMP TABLE _autodev_affected_projects (
    "Id" integer PRIMARY KEY
) ON COMMIT DROP;

INSERT INTO _autodev_affected_projects ("Id")
SELECT DISTINCT "ProjectId"
FROM _autodev_finished_runs
UNION
SELECT p."Id"
FROM "Projects" AS p
WHERE p."LastRunStatus" IN ('Pending', 'Running')
ON CONFLICT ("Id") DO NOTHING;

WITH updated AS (
    UPDATE "Projects" AS p
    SET
        "LastRunStatus" = opts.target_status,
        "LastRunAt" = CURRENT_TIMESTAMP,
        "LastError" = CASE
            WHEN opts.target_status = 'Success' THEN NULL
            ELSE COALESCE(NULLIF(p."LastError", ''), opts.reason)
        END,
        "CurrentTask" = CASE
            WHEN opts.clear_current_task THEN NULL
            ELSE p."CurrentTask"
        END
    FROM _autodev_finish_options AS opts, _autodev_affected_projects AS ap
    WHERE ap."Id" = p."Id"
    RETURNING p."Id", p."Name", p."LastRunStatus", p."CurrentTask"
)
SELECT
    (SELECT count(*) FROM _autodev_finished_runs) AS finished_runs,
    (SELECT count(*) FROM updated) AS updated_projects;

\echo ''
\echo 'Finished runs:'
TABLE _autodev_finished_runs;

\echo ''
\echo 'Affected projects:'
SELECT p."Id", p."Name", p."LastRunStatus", p."LastRunAt", p."LastError", p."CurrentTask"
FROM "Projects" AS p
JOIN _autodev_affected_projects AS ap ON ap."Id" = p."Id"
ORDER BY p."Id";

\if :dry_run
ROLLBACK;
\echo ''
\echo 'DRY RUN: rolled back changes.'
\else
COMMIT;
\echo ''
\echo 'Committed active run finish updates.'
\endif
