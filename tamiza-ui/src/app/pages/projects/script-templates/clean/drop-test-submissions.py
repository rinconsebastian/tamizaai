# Script 1: remove the submissions that are not real data (tests, duplicates).
# Adapt the two constants: they are the only parts that depend on your form.
TEST_COLUMN = "_uuid"  # a column of raw["main"] that tells test submissions apart
TEST_PREFIX = "test"  # submissions whose TEST_COLUMN starts with this text are dropped


def clean(raw, ctx):
    # %% Look at the flattened tables
    main = raw["main"]
    ctx.show(main, "main")

    # %% Drop test submissions and duplicates
    is_test = main[TEST_COLUMN].astype("string").str.lower().str.startswith(TEST_PREFIX, na=False)
    cleaned = main[~is_test].drop_duplicates(subset="_uuid")
    ctx.log.info("Dropped %d submissions", len(main) - len(cleaned))
    ctx.show(cleaned, "cleaned main")

    # %% Return the tables
    return {**raw, "main": cleaned}
