# Indicator: how many submissions there are in each group.
# Adapt the constant: the columns to count by, such as questions of your form (["municipality"]).
# List every column the dashboard groups or filters by (["municipality", "sex", "round"]): a widget adds the rows
# back up for the columns it does not group by, and a dashboard filter can only narrow a table that has its column.
GROUPS = ["_version"]


def compute(cleaned, ctx):
    # %% Look at the cleaned data
    main = cleaned["main"]
    ctx.show(main, "main")

    # %% Count the submissions of each group
    counts = main.groupby(GROUPS, dropna=False).size().rename("value").reset_index()
    ctx.show(counts, "counts")

    # %% Return the indicator: the groups and a numeric column called value
    return counts
