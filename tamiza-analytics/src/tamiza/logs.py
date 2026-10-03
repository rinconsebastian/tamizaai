"""JSON log lines on stdout, matching the API's structured console logs."""

import json
import logging
import sys
from datetime import UTC, datetime


class JsonFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        timestamp = datetime.fromtimestamp(record.created, UTC).isoformat(timespec="milliseconds")
        entry = {
            "Timestamp": timestamp.replace("+00:00", "Z"),
            "LogLevel": record.levelname,
            "Category": record.name,
            "Message": record.getMessage(),
        }
        if record.exc_info:
            entry["Exception"] = self.formatException(record.exc_info)
        return json.dumps(entry)


def configure() -> None:
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JsonFormatter())
    logging.basicConfig(level=logging.INFO, handlers=[handler], force=True)
