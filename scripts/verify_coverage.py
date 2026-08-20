#!/usr/bin/env python3
"""Merge Cobertura reports and enforce the exact-head line coverage floor."""

from __future__ import annotations

import argparse
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


@dataclass(frozen=True)
class CoverageSummary:
    covered: int
    valid: int

    @property
    def percent(self) -> float:
        return 100.0 * self.covered / self.valid if self.valid else 0.0


LineKey = tuple[str, str, int]


def normalize_filename(filename: str) -> str:
    """Make paths from different test projects comparable within one CI run."""
    normalized = filename.replace("\\", "/")
    for marker in ("/src/", "/tests/"):
        if marker in normalized:
            return f"{marker.strip('/')}/{normalized.split(marker, maxsplit=1)[1]}"
    return normalized.removeprefix("./").lstrip("/")


def discover_reports(root: Path) -> list[Path]:
    return sorted(root.rglob("coverage.cobertura.xml"))


def collect_line_hits(reports: list[Path]) -> dict[LineKey, int]:
    """Union executable lines, retaining the highest hit count for duplicates."""
    line_hits: dict[LineKey, int] = {}

    for report in reports:
        root = ET.parse(report).getroot()
        for package in root.findall("./packages/package"):
            package_name = package.get("name", "<unknown>")
            for coverage_class in package.findall("./classes/class"):
                filename = normalize_filename(coverage_class.get("filename", "<unknown>"))
                for line in coverage_class.findall("./lines/line"):
                    number = int(line.attrib["number"])
                    hits = int(line.get("hits", "0"))
                    key = (package_name, filename, number)
                    line_hits[key] = max(line_hits.get(key, 0), hits)

    return line_hits


def summarize(line_hits: dict[LineKey, int]) -> tuple[CoverageSummary, dict[str, CoverageSummary]]:
    package_counts: dict[str, list[int]] = defaultdict(lambda: [0, 0])

    for (package_name, _filename, _number), hits in line_hits.items():
        package_counts[package_name][1] += 1
        if hits > 0:
            package_counts[package_name][0] += 1

    by_package = {
        package_name: CoverageSummary(covered=counts[0], valid=counts[1])
        for package_name, counts in package_counts.items()
    }
    overall = CoverageSummary(
        covered=sum(summary.covered for summary in by_package.values()),
        valid=sum(summary.valid for summary in by_package.values()),
    )
    return overall, by_package


def verify(reports_root: Path, minimum_line_rate: float) -> bool:
    reports = discover_reports(reports_root)
    if not reports:
        raise ValueError(f"Cobertura reports not found under {reports_root}")

    overall, by_package = summarize(collect_line_hits(reports))
    if overall.valid == 0:
        raise ValueError("Cobertura reports contain no executable lines")

    print(f"Coverage reports: {len(reports)}")
    for package_name, summary in sorted(by_package.items()):
        print(f"{package_name}: {summary.covered}/{summary.valid} ({summary.percent:.2f}%)")
    print(f"TOTAL: {overall.covered}/{overall.valid} ({overall.percent:.2f}%)")
    print(f"Required minimum: {minimum_line_rate:.2f}%")

    return overall.percent >= minimum_line_rate


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--reports",
        type=Path,
        required=True,
        help="Directory containing one or more coverage.cobertura.xml reports",
    )
    parser.add_argument(
        "--minimum-line-rate",
        type=float,
        required=True,
        help="Required aggregate line coverage percentage (0..100)",
    )
    args = parser.parse_args(argv)
    if not 0.0 <= args.minimum_line_rate <= 100.0:
        parser.error("--minimum-line-rate must be between 0 and 100")
    return args


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv if argv is not None else sys.argv[1:])
    try:
        passed = verify(args.reports, args.minimum_line_rate)
    except (ET.ParseError, OSError, ValueError) as error:
        print(f"Coverage gate error: {error}", file=sys.stderr)
        return 2

    if not passed:
        print("Coverage gate failed", file=sys.stderr)
        return 1

    print("Coverage gate passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
