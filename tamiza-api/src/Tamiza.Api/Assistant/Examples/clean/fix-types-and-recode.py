import pandas as pd

# Script 1: give columns their real types and turn empty text into missing values.
# Adapt the constants: the date column to convert, and the text columns whose "" should be missing.
DATE_COLUMN = "_submission_time"
TEXT_COLUMNS = ["_version"]


def clean(raw, ctx):
    # %% Look at the column types
    main = raw["main"].copy()
    types = main.dtypes.astype(str).rename("type").rename_axis("column").reset_index()
    ctx.show(types, "column types")

    # %% Convert dates and empty text
    main[DATE_COLUMN] = pd.to_datetime(main[DATE_COLUMN], errors="coerce")
    for column in TEXT_COLUMNS:
        main[column] = main[column].replace("", pd.NA)
    ctx.show(main, "converted main")

    # %% Return the tables
    return {**raw, "main": main}
