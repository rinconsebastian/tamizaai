-- Scheduled dashboard emails: at most one per dashboard, removed with it. The settings (recipients, ISO weekdays 1-7, local time,
-- active, the saver's locale) and the send state. last_scheduled_at is written only when the scheduler takes a day; not_before only
-- by a save made after that day's time, so the day is not due. Addresses of recipients are stored here and nowhere else. Removing
-- the email clears its recipients and marks it removed_at, so the day it was sent and the last "send now" stay known if it is saved again.

CREATE TABLE tamiza.dashboard_reports (
    dashboard_id uuid NOT NULL,
    recipients text[] NOT NULL,
    days smallint[] NOT NULL,
    send_time time NOT NULL,
    active boolean NOT NULL,
    locale text NOT NULL,
    updated_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    not_before timestamp with time zone,
    removed_at timestamp with time zone,
    last_scheduled_at timestamp with time zone,
    send_now_pending boolean NOT NULL DEFAULT false,
    send_now_requested_at timestamp with time zone,
    last_sent_at timestamp with time zone,
    last_failed_at timestamp with time zone,
    last_failure text,
    last_failure_address text,
    CONSTRAINT pk_dashboard_reports PRIMARY KEY (dashboard_id),
    CONSTRAINT ck_dashboard_reports_recipients CHECK (cardinality(recipients) <= 20 AND (removed_at IS NOT NULL OR cardinality(recipients) >= 1)),
    CONSTRAINT ck_dashboard_reports_days CHECK (cardinality(days) BETWEEN 1 AND 7 AND days <@ ARRAY[1,2,3,4,5,6,7]::smallint[]),
    CONSTRAINT fk_dashboard_reports_dashboards_dashboard_id FOREIGN KEY (dashboard_id) REFERENCES tamiza.dashboards (id) ON DELETE CASCADE,
    CONSTRAINT fk_dashboard_reports_users_updated_by FOREIGN KEY (updated_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

CREATE INDEX ix_dashboard_reports_updated_by ON tamiza.dashboard_reports (updated_by);
