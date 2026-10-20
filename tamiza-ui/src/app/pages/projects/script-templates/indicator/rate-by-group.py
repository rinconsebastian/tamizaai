# Indicator: the share of submissions that meet a condition, in each group.
# Adapt the constant GROUPS, and the condition in counted() below.
# List every column the dashboard groups or filters by (["municipality", "sex", "round"]): a widget plots the
# numerator's sum over the denominator's for the columns it does not group by, so the rate stays right, and a
# dashboard filter can only narrow a table that has its column.
GROUPS = ["_version"]


def counted(main):
    """The rows that count towards the numerator, for example main["water_source"] == "Piped"."""
    return main["_uuid"].notna()


def compute(cleaned, ctx):
    # %% Look at the cleaned data
    main = cleaned["main"]
    ctx.show(main, "main")

    # %% Count the rows that meet the condition in each group
    rows = (
        main.assign(_counted=counted(main))
        .groupby(GROUPS, dropna=False)
        .agg(numerator=("_counted", "sum"), denominator=("_counted", "size"))
        .reset_index()
    )
    rows["numerator"] = rows["numerator"].astype(int)
    rows["value"] = rows["numerator"] / rows["denominator"]
    ctx.show(rows, "rate")

    # %% Return the indicator: value, and the two sums a widget needs to combine groups
    return rows[[*GROUPS, "value", "numerator", "denominator"]]
