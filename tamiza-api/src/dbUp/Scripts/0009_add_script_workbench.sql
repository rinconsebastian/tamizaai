-- Script workbench: a sample test can stop when execution first reaches a `# %%` marker of the tested code. The column holds
-- the 1-based line of that marker; null means a whole test.

ALTER TABLE tamiza.script_tests
    ADD COLUMN stop_line integer,
    ADD CONSTRAINT ck_script_tests_stop_line CHECK (stop_line > 0);
