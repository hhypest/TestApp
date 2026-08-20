#!/usr/bin/env python3
"""Reject mutable GitHub Action and external container references."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import re
import shlex
import sys


ACTION_REFERENCE = re.compile(r"^\s*-?\s*uses:\s*(?P<reference>[^\s#]+)")
IMAGE_DECLARATION = re.compile(r"^\s*image:\s*(?P<reference>.+?)\s*$")
RUNNER_DECLARATION = re.compile(r"^\s*runs-on:\s*(?P<reference>[^\s#]+)")
DOTNET_VERSION = re.compile(r"^\s*dotnet-version:\s*(?P<reference>[^\s#]+)")
DOCKERFILE_FROM = re.compile(r"^\s*FROM\s+(?P<reference>\S+)", re.IGNORECASE)
IMAGE_DEFAULT = re.compile(
    r"\$\{(?P<name>IMAGE|[A-Z][A-Z0-9_]*_IMAGE):-(?P<reference>[^}]+)\}"
)
WORKFLOW_RUN = re.compile(r"^(?P<indent>\s+)-?\s*run:\s*(?P<value>.*?)\s*$")
PINNED_ACTION = re.compile(r"^[^@\s]+@[0-9a-f]{40}$")
PINNED_IMAGE = re.compile(r"^[^@\s]+@sha256:[0-9a-f]{64}$")
PINNED_RUNNER = re.compile(r"^ubuntu-[0-9]{2}\.[0-9]{2}$")
PINNED_DOTNET_VERSION = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+$")
STAGING_IMAGE_PLACEHOLDER = re.compile(
    r"^TESTAPP_IMAGE=[^\s@]+@sha256:[0-9a-f]{64}$",
    re.MULTILINE,
)


@dataclass(frozen=True)
class Reference:
    path: Path
    line: int
    kind: str
    value: str


DOCKER_FLAGS_WITHOUT_VALUE = {
    "--detach",
    "--init",
    "--interactive",
    "--privileged",
    "--read-only",
    "--rm",
    "--tty",
    "-d",
    "-i",
    "-t",
}


def workflow_files(root: Path) -> list[Path]:
    directory = root / ".github" / "workflows"
    return sorted((*directory.glob("*.yml"), *directory.glob("*.yaml")))


def compose_files(root: Path) -> list[Path]:
    return sorted((*root.rglob("compose*.yml"), *root.rglob("compose*.yaml")))


def dockerfiles(root: Path) -> list[Path]:
    return sorted(path for path in root.rglob("Dockerfile*") if path.is_file())


def strip_yaml_value(value: str) -> str:
    value = value.strip()
    if value[:1] in {"'", '"'} and value[-1:] == value[:1]:
        return value[1:-1]
    return value


def logical_shell_commands(lines: list[tuple[int, str]]) -> list[tuple[int, str]]:
    commands: list[tuple[int, str]] = []
    buffer: list[str] = []
    start_line = 0

    for line_number, line in lines:
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if not buffer:
            start_line = line_number
        continued = stripped.endswith("\\")
        buffer.append(stripped.removesuffix("\\").rstrip())
        if not continued:
            commands.append((start_line, " ".join(buffer)))
            buffer = []

    if buffer:
        commands.append((start_line, " ".join(buffer)))
    return commands


def workflow_commands(path: Path) -> list[tuple[int, str]]:
    lines = path.read_text(encoding="utf-8").splitlines()
    commands: list[tuple[int, str]] = []
    index = 0

    while index < len(lines):
        match = WORKFLOW_RUN.match(lines[index])
        if not match:
            index += 1
            continue

        value = match["value"]
        line_number = index + 1
        if value not in {"|", "|-", "|+", ">", ">-", ">+"}:
            commands.append((line_number, value))
            index += 1
            continue

        base_indent = len(match["indent"])
        block: list[tuple[int, str]] = []
        index += 1
        while index < len(lines):
            line = lines[index]
            indentation = len(line) - len(line.lstrip())
            if line.strip() and indentation <= base_indent:
                break
            block.append((index + 1, line))
            index += 1

        if value.startswith(">"):
            folded = " ".join(line.strip() for _number, line in block if line.strip())
            commands.append((block[0][0] if block else line_number, folded))
        else:
            commands.extend(logical_shell_commands(block))

    return commands


def docker_run_images(command: str) -> list[str]:
    try:
        tokens = shlex.split(command, comments=True, posix=True)
    except ValueError:
        return []

    images: list[str] = []
    index = 0
    while index + 1 < len(tokens):
        if tokens[index] != "docker" or tokens[index + 1] != "run":
            index += 1
            continue

        index += 2
        while index < len(tokens):
            token = tokens[index]
            if not token.startswith("-"):
                images.append(token)
                break
            if token in DOCKER_FLAGS_WITHOUT_VALUE or "=" in token:
                index += 1
            else:
                index += 2
        index += 1

    return images


def scan_references(root: Path) -> list[Reference]:
    references: list[Reference] = []

    for path in workflow_files(root):
        lines = path.read_text(encoding="utf-8").splitlines()
        for line_number, line in enumerate(lines, 1):
            if match := ACTION_REFERENCE.match(line):
                references.append(
                    Reference(path, line_number, "action", strip_yaml_value(match["reference"]))
                )
            if match := IMAGE_DECLARATION.match(line):
                references.append(
                    Reference(path, line_number, "image", strip_yaml_value(match["reference"]))
                )
            if match := RUNNER_DECLARATION.match(line):
                references.append(
                    Reference(path, line_number, "runner", strip_yaml_value(match["reference"]))
                )
            if match := DOTNET_VERSION.match(line):
                references.append(
                    Reference(path, line_number, "runtime", strip_yaml_value(match["reference"]))
                )
        for line_number, command in workflow_commands(path):
            for image in docker_run_images(command):
                references.append(Reference(path, line_number, "image", image))

    for path in compose_files(root):
        for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if match := IMAGE_DECLARATION.match(line):
                references.append(
                    Reference(path, line_number, "image", strip_yaml_value(match["reference"]))
                )

    for path in dockerfiles(root):
        for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if match := DOCKERFILE_FROM.match(line):
                references.append(Reference(path, line_number, "image", match["reference"]))

    for path in sorted((root / "scripts").glob("*.sh")):
        lines = path.read_text(encoding="utf-8").splitlines()
        for line_number, line in enumerate(lines, 1):
            for match in IMAGE_DEFAULT.finditer(line):
                references.append(Reference(path, line_number, "image", match["reference"]))
        for line_number, command in logical_shell_commands(
            list(enumerate(lines, start=1))
        ):
            for image in docker_run_images(command):
                references.append(Reference(path, line_number, "image", image))

    return references


def is_allowed_image(reference: Reference) -> bool:
    if PINNED_IMAGE.fullmatch(reference.value):
        return True
    if reference.value.startswith("$"):
        return True
    if reference.value == "scratch" or reference.value.startswith("testapp-api:"):
        return True
    return reference.path.name == "compose.staging.yaml" and reference.value.startswith(
        "${TESTAPP_IMAGE:"
    )


def find_issues(root: Path) -> tuple[list[str], list[Reference]]:
    references = scan_references(root)
    issues: list[str] = []
    resolved_pins: dict[tuple[str, str], Reference] = {}

    for reference in references:
        relative_path = reference.path.relative_to(root)
        if reference.kind == "action":
            if reference.value.startswith("./"):
                continue
            if reference.value.startswith("docker://"):
                image = Reference(
                    reference.path,
                    reference.line,
                    "image",
                    reference.value.removeprefix("docker://"),
                )
                if is_allowed_image(image):
                    if PINNED_IMAGE.fullmatch(image.value):
                        key = (image.kind, image.value.rsplit("@", maxsplit=1)[0])
                        previous = resolved_pins.setdefault(key, image)
                        if previous.value != image.value:
                            issues.append(
                                f"{relative_path}:{reference.line}: inconsistent digest for "
                                f"{key[1]!r}; first seen as {previous.value!r}"
                            )
                    continue
            elif PINNED_ACTION.fullmatch(reference.value):
                key = (reference.kind, reference.value.rsplit("@", maxsplit=1)[0])
                previous = resolved_pins.setdefault(key, reference)
                if previous.value != reference.value:
                    issues.append(
                        f"{relative_path}:{reference.line}: inconsistent SHA for {key[1]!r}; "
                        f"first seen as {previous.value!r}"
                    )
                continue
        elif is_allowed_image(reference):
            if PINNED_IMAGE.fullmatch(reference.value):
                key = (reference.kind, reference.value.rsplit("@", maxsplit=1)[0])
                previous = resolved_pins.setdefault(key, reference)
                if previous.value != reference.value:
                    issues.append(
                        f"{relative_path}:{reference.line}: inconsistent digest for {key[1]!r}; "
                        f"first seen as {previous.value!r}"
                    )
            continue
        elif reference.kind == "runner" and PINNED_RUNNER.fullmatch(reference.value):
            continue
        elif reference.kind == "runtime" and PINNED_DOTNET_VERSION.fullmatch(
            reference.value
        ):
            continue

        issues.append(
            f"{relative_path}:{reference.line}: mutable {reference.kind} reference "
            f"{reference.value!r}"
        )

    staging_example = root / ".env.staging.example"
    if staging_example.exists() and not STAGING_IMAGE_PLACEHOLDER.search(
        staging_example.read_text(encoding="utf-8")
    ):
        issues.append(
            ".env.staging.example: TESTAPP_IMAGE must demonstrate an immutable sha256 digest"
        )

    return issues, references


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="Repository root (defaults to the parent of scripts/)",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv if argv is not None else sys.argv[1:])
    issues, references = find_issues(args.root.resolve())
    if issues:
        print("Immutable reference gate failed:", file=sys.stderr)
        for issue in issues:
            print(f"- {issue}", file=sys.stderr)
        return 1

    actions = sum(reference.kind == "action" for reference in references)
    images = sum(reference.kind == "image" for reference in references)
    runners = sum(reference.kind == "runner" for reference in references)
    runtimes = sum(reference.kind == "runtime" for reference in references)
    print(
        f"Immutable references verified: {actions} actions, {images} container images, "
        f"{runners} runner labels, {runtimes} runtime declarations"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
