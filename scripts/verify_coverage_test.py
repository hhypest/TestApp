import contextlib
import io
from pathlib import Path
import tempfile
import textwrap
import unittest

import verify_coverage


def write_report(path: Path, packages: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        textwrap.dedent(
            f"""\
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                {packages}
              </packages>
            </coverage>
            """
        ),
        encoding="utf-8",
    )


class VerifyCoverageTests(unittest.TestCase):
    def test_reports_are_unioned_without_double_counting_lines(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            root = Path(temp_directory)
            write_report(
                root / "core" / "coverage.cobertura.xml",
                """
                <package name="TestApp.Core">
                  <classes>
                    <class filename="/agent/work/src/TestApp.Core/Result.cs">
                      <lines>
                        <line number="10" hits="0" />
                        <line number="11" hits="2" />
                      </lines>
                    </class>
                  </classes>
                </package>
                """,
            )
            write_report(
                root / "integration" / "coverage.cobertura.xml",
                """
                <package name="TestApp.Core">
                  <classes>
                    <class filename="C:\\agent\\work\\src\\TestApp.Core\\Result.cs">
                      <lines>
                        <line number="10" hits="3" />
                        <line number="11" hits="0" />
                        <line number="12" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
                <package name="TestApp.Domain">
                  <classes>
                    <class filename="src/TestApp.Domain/Test.cs">
                      <lines><line number="20" hits="0" /></lines>
                    </class>
                  </classes>
                </package>
                """,
            )

            reports = verify_coverage.discover_reports(root)
            overall, by_package = verify_coverage.summarize(
                verify_coverage.collect_line_hits(reports)
            )

            self.assertEqual(2, len(reports))
            self.assertEqual(verify_coverage.CoverageSummary(2, 4), overall)
            self.assertEqual(
                verify_coverage.CoverageSummary(2, 3),
                by_package["TestApp.Core"],
            )
            self.assertEqual(
                verify_coverage.CoverageSummary(0, 1),
                by_package["TestApp.Domain"],
            )

    def test_threshold_is_inclusive_and_failure_is_reported(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            root = Path(temp_directory)
            write_report(
                root / "coverage.cobertura.xml",
                """
                <package name="TestApp.Core">
                  <classes>
                    <class filename="src/TestApp.Core/Result.cs">
                      <lines>
                        <line number="1" hits="1" />
                        <line number="2" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
                """,
            )

            with contextlib.redirect_stdout(io.StringIO()):
                self.assertTrue(verify_coverage.verify(root, 50.0))
                self.assertFalse(verify_coverage.verify(root, 50.01))

    def test_missing_reports_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            with self.assertRaisesRegex(ValueError, "Cobertura reports not found"):
                verify_coverage.verify(Path(temp_directory), 90.0)


if __name__ == "__main__":
    unittest.main()
