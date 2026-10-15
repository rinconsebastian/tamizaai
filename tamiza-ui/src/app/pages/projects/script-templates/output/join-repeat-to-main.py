# Script 2: add to each submission the number of rows it has in a repeat group.
# Adapt the constant: the name of a repeat group of your form (a table of cleaned besides "main").
REPEAT = "members"


def prepare_output(cleaned, ctx):
    # %% Look at the tables
    main = cleaned["main"]
    ctx.show(sorted(cleaned), "tables")

    # %% Count the rows of the repeat group in each submission
    if REPEAT in cleaned:
        counts = cleaned[REPEAT].groupby("_parent_id").size().rename(f"{REPEAT}_count")
        main = main.merge(counts, how="left", left_on="_id", right_index=True)
        main[f"{REPEAT}_count"] = main[f"{REPEAT}_count"].fillna(0).astype(int)
    else:
        ctx.log.warning("There is no table %r: set REPEAT to a repeat group of your form.", REPEAT)
    ctx.show(main, "main with counts")

    # %% Return the tables
    return {"main": main}
