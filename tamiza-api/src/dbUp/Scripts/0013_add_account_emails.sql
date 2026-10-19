-- Account emails: the identity-provider accounts Tamiza created for invited emails, and when an invitation email was last sent to
-- each. Only these accounts may be sent an invitation email again; accounts made elsewhere are never touched.

CREATE TABLE tamiza.invited_accounts (
    account_id character varying(255) NOT NULL,
    normalized_email character varying(320) NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    last_email_at timestamp with time zone,
    CONSTRAINT pk_invited_accounts PRIMARY KEY (account_id),
    CONSTRAINT fk_invited_accounts_users_created_by FOREIGN KEY (created_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

CREATE INDEX ix_invited_accounts_created_by ON tamiza.invited_accounts (created_by);

CREATE INDEX ix_invited_accounts_normalized_email ON tamiza.invited_accounts (normalized_email);
