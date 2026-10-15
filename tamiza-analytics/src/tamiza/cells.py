"""Running a script through a cell: stopping where execution first reaches a ``# %%`` marker.

The marker is a comment, so it has no place in the syntax tree. It is found by its line and indentation: a call to the
stop function is inserted into the statement list the marker sits in, between the two statements it separates, and
every other node keeps its own line, so tracebacks are unchanged.
"""

import ast
import re

MARKER = re.compile(r"^[ \t]*# %%")
STOP_NAME = "__tamiza_stop__"


class CellStop(BaseException):
    """Raised by the inserted call. A ``BaseException`` so a script's ``except Exception`` does not swallow it."""

    def __init__(self, line: int):
        super().__init__(line)
        self.line = line


class MarkerError(Exception):
    """The marker cannot be given a place between two statements; the message says why."""


def is_marker(line: str) -> bool:
    return MARKER.match(line) is not None


def insert_stop(tree: ast.Module, source: str, line: int) -> None:
    """Inserts the stop call for the marker on ``line`` (1-based) of ``source`` into ``tree``."""
    lines = source.split("\n")
    if not 1 <= line <= len(lines) or not is_marker(lines[line - 1]):
        raise MarkerError(f"Line {line} is not a # %% marker.")
    text = lines[line - 1].rstrip("\r")
    column = len(text) - len(text.lstrip(" \t"))
    found = _place(tree.body, line, column)
    if found is None:
        raise MarkerError(
            f"The # %% marker on line {line} must be on its own line between two statements, "
            "not inside a statement that continues over several lines."
        )
    statements, index = found
    call = ast.Expr(ast.Call(ast.Name(STOP_NAME, ast.Load()), [], []))
    for node in ast.walk(call):
        if isinstance(node, ast.expr | ast.stmt):
            node.lineno = node.end_lineno = line
            node.col_offset = node.end_col_offset = column
    statements.insert(index, call)


def _start(statement: ast.stmt) -> int:
    decorators = getattr(statement, "decorator_list", [])
    return min([statement.lineno, *(d.lineno for d in decorators)])


def _bodies(statement: ast.stmt) -> list[list[ast.stmt]]:
    """The statement lists inside a compound statement, in source order."""
    match statement:
        case ast.FunctionDef() | ast.AsyncFunctionDef() | ast.ClassDef() | ast.With() | ast.AsyncWith():
            return [statement.body]
        case ast.For() | ast.AsyncFor() | ast.While() | ast.If():
            return [statement.body, *([statement.orelse] if statement.orelse else [])]
        case ast.Try() | ast.TryStar():
            bodies = [statement.body, *(handler.body for handler in statement.handlers)]
            return [*bodies, *(body for body in (statement.orelse, statement.finalbody) if body)]
        case ast.Match():
            return [case.body for case in statement.cases]
        case _:
            return []


def _place(statements: list[ast.stmt], line: int, column: int):
    """The statement list and index to insert at: the deepest list at the marker's indentation that it lies in."""
    for statement in statements:
        if _start(statement) <= line <= statement.end_lineno:
            for body in _bodies(statement):
                found = _place(body, line, column)
                if found is not None:
                    return found
            return None  # inside a statement that continues over several lines
    index = sum(1 for statement in statements if statement.end_lineno < line)
    indent = statements[0].col_offset
    if column == indent:
        return statements, index
    if column > indent and index > 0:
        return _tail(statements[index - 1], column)
    return None


def _tail(statement: ast.stmt, column: int):
    """A marker after the last statement of the block that ends ``statement``, indented as that block is."""
    bodies = _bodies(statement)
    if not bodies:
        return None
    body = bodies[-1]
    indent = body[0].col_offset
    if column == indent:
        return body, len(body)
    return _tail(body[-1], column) if column > indent else None
