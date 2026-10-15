"""``ctx.show`` on your own computer: displays the value where you are working instead of recording it for a test."""

from collections.abc import Callable

import pandas as pd


class NotebookShow:
    """
    A ``show`` for ``Context``. In a Jupyter notebook it displays the name and then the value, so pandas renders its
    own table; anywhere else it prints both. Unlike a sample test there is no limit on calls and no snapshot: what was
    displayed stays in the notebook.
    """

    def __init__(self) -> None:
        self._calls = 0

    def __call__(self, data: object, name: str | None = None) -> None:
        self._calls += 1
        label = str(name) if name is not None else f"shown {self._calls}"
        if isinstance(data, pd.DataFrame | pd.Series):
            label = f"{label} ({len(data):,} rows)"
        print(label)
        display = _notebook_display()
        if display is not None:
            display(data)
        else:
            print(data)


def _notebook_display() -> Callable[[object], None] | None:
    """IPython's ``display`` when running inside IPython or Jupyter, else None. IPython is not required."""
    try:
        from IPython import get_ipython
    except ImportError:
        return None
    if get_ipython() is None:
        return None
    from IPython.display import display

    return display
