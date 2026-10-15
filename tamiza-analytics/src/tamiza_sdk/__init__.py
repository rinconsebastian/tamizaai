"""Tamiza SDK: write and check Tamiza scripts on your own computer.

The same code the Tamiza worker uses to build ``ctx``, read tables and validate what a script returns, without the
database. A download from the Scripts section ("Download sample") includes this package.
"""

__version__ = "0.1.0"

from tamiza_sdk.bundle import EnvironmentWarning  # noqa: E402
from tamiza_sdk.checking import CheckResult, check  # noqa: E402
from tamiza_sdk.contract import ScriptError  # noqa: E402
from tamiza_sdk.local import load_context, load_tables  # noqa: E402

__all__ = ["CheckResult", "EnvironmentWarning", "ScriptError", "__version__", "check", "load_context", "load_tables"]
