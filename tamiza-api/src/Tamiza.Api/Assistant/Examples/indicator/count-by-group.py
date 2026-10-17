# Indicator: how many submissions there are in each group.
# Adapt the constant: the column to count by, such as a question of your form ("municipality").
GROUP = "_version"


def compute(cleaned, ctx):
    # %% Look at the cleaned data
    main = cleaned["main"]
    ctx.show(main, "main")

    # %% Count the submissions of each group
    counts = main.groupby(GROUP, dropna=False).size().rename("value").reset_index()
    ctx.show(counts, "counts")

    # %% Return the indicator: the group and a numeric column called value
    return counts
