# Script 2: choose, order and rename the columns the clean data dashboard shows.
# Adapt the constants: COLUMNS are columns of cleaned["main"]; RENAME maps an old name to a new one
# (lowercase letters, digits and underscores).
COLUMNS = ["_id", "_submission_time"]
RENAME = {}


def prepare_output(cleaned, ctx):
    # %% Look at the cleaned data
    main = cleaned["main"]
    ctx.show(main, "main")

    # %% Select and rename the columns
    view = main[COLUMNS].rename(columns=RENAME)
    ctx.show(view, "dashboard columns")

    # %% Return the tables
    return {"main": view}
