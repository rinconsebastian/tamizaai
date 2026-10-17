# Spec Delta

## ADDED Requirements

### Requirement: Check of unavailable imports
With the other checks while editing, the editor SHALL mark an import, at any level of the script, of a module that is neither one of the libraries the worker provides to scripts nor part of the Python standard library, with a warning that names the module and lists the available libraries. The mark follows the rule that checks never block.

#### Scenario: Library the worker does not have
- **WHEN** an indicator script contains `from sklearn.linear_model import LinearRegression`
- **THEN** the editor marks that line with a warning that `sklearn` is not available to scripts, listing the libraries that are

#### Scenario: Available libraries and the standard library
- **WHEN** a script imports `pandas as pd`, `numpy as np`, `geopandas`, `shapely.geometry`, `duckdb` and `from datetime import date`
- **THEN** the editor marks none of those imports

#### Scenario: Import inside the contract function
- **WHEN** an import of `requests` appears inside `compute`, not at the top of the script
- **THEN** the editor marks it the same way as a top-level import

#### Scenario: Saving with an import warning
- **WHEN** the editor marks `import statsmodels.api as sm` and the analyst saves
- **THEN** the script is saved as a new version

### Requirement: Check of imports scripts never need
With the other checks while editing, the editor SHALL mark an import of `os`, `sys`, `subprocess`, `socket`, `shutil`, `ctypes`, `importlib`, `http` or `urllib`, or of a submodule of one of them, and any call of `__import__`, with a warning that scripts receive their data from Tamiza and need neither files, processes nor the network. The mark follows the rule that checks never block.

#### Scenario: Process or network module
- **WHEN** a script contains `import subprocess` or `from urllib.request import urlopen`
- **THEN** the editor marks the line with the warning that scripts need neither files, processes nor the network

#### Scenario: Dynamic import
- **WHEN** a script calls `__import__("os")`
- **THEN** the editor marks the call with the same warning

#### Scenario: Standard library modules that are fine
- **WHEN** a script imports `re`, `math`, `json`, `datetime` and `unicodedata`
- **THEN** the editor marks none of them
