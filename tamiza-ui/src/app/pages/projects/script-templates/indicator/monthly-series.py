import pandas as pd

# Indicator: how many submissions arrived each month, for a line chart.
# Adapt the constant: the date column of cleaned["main"] to group by.
DATE_COLUMN = "_submission_time"


def compute(cleaned, ctx):
    # %% Look at the cleaned data
    main = cleaned["main"]
    ctx.show(main, "main")

    # %% Count the submissions of each month
    month = pd.to_datetime(main[DATE_COLUMN]).dt.strftime("%Y-%m")
    series = main.groupby(month).size().rename("value").rename_axis("month").reset_index()
    ctx.show(series, "series")

    # %% Return the indicator: month as text, so it is a dimension, and value
    return series
