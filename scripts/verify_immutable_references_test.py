from pathlib import Path
import tempfile
import textwrap
import unittest

import verify_immutable_references


DIGEST = "sha256:" + "1" * 64
OTHER_DIGEST = "sha256:" + "2" * 64
ACTION_SHA = "a" * 40
OTHER_ACTION_SHA = "b" * 40


def write(root: Path, relative_path: str, content: str) -> None:
    path = root / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(textwrap.dedent(content).lstrip(), encoding="utf-8")


class VerifyImmutableReferencesTests(unittest.TestCase):
    def test_accepts_sha_pins_and_explicit_local_or_staging_images(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            root = Path(temp_directory)
            write(
                root,
                ".github/workflows/ci.yml",
                f"""
                jobs:
                  test:
                    runs-on: ubuntu-24.04
                    services:
                      postgres:
                        image: postgres:18@{DIGEST}
                    steps:
                      - uses: actions/checkout@{ACTION_SHA} # v4
                      - name: Set up .NET
                        with:
                          dotnet-version: '10.0.400'
                      - run: |
                          docker run --rm \\
                            postgres:18@{DIGEST} \\
                            psql --version
                """,
            )
            write(root, "Dockerfile", f"FROM example/runtime:1@{DIGEST}\n")
            write(
                root,
                "compose.yaml",
                "services:\n  api:\n    image: testapp-api:local\n",
            )
            write(
                root,
                "compose.staging.yaml",
                "services:\n  api:\n    image: ${TESTAPP_IMAGE:?}\n",
            )
            write(
                root,
                "scripts/backup.sh",
                f'POSTGRES_IMAGE="${{POSTGRES_IMAGE:-postgres:18@{DIGEST}}}"\n',
            )
            write(
                root,
                ".env.staging.example",
                f"TESTAPP_IMAGE=ghcr.io/example/testapp@{DIGEST}\n",
            )

            issues, references = verify_immutable_references.find_issues(root)

            self.assertEqual([], issues)
            self.assertGreaterEqual(len(references), 6)

    def test_rejects_mutable_actions_and_container_tags_in_every_supported_context(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            root = Path(temp_directory)
            write(
                root,
                ".github/workflows/ci.yml",
                """
                jobs:
                  test:
                    runs-on: ubuntu-latest
                    services:
                      postgres:
                        image: postgres:18
                    steps:
                      - uses: actions/checkout@v4
                      - name: Set up .NET
                        with:
                          dotnet-version: '10.0.x'
                      - run: docker run --rm postman/newman:6-alpine --version
                """,
            )
            write(root, "Dockerfile", "FROM example/runtime:latest\n")
            write(
                root,
                "compose.yaml",
                "services:\n  metrics:\n    image: prom/prometheus:v3.7.3\n",
            )
            write(
                root,
                "scripts/backup.sh",
                'POSTGRES_IMAGE="${POSTGRES_IMAGE:-postgres:18}"\n'
                'IMAGE="${IMAGE:-example/tool:latest}"\n',
            )

            issues, _references = verify_immutable_references.find_issues(root)

            self.assertEqual(9, len(issues))
            self.assertTrue(any("actions/checkout@v4" in issue for issue in issues))
            self.assertTrue(any("postman/newman:6-alpine" in issue for issue in issues))
            self.assertTrue(any("Dockerfile:1" in issue for issue in issues))

    def test_rejects_inconsistent_pins_for_the_same_named_input(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            root = Path(temp_directory)
            write(
                root,
                ".github/workflows/first.yml",
                f"""
                jobs:
                  test:
                    services:
                      postgres:
                        image: postgres:18@{DIGEST}
                    steps:
                      - uses: actions/checkout@{ACTION_SHA}
                """,
            )
            write(
                root,
                ".github/workflows/second.yml",
                f"""
                jobs:
                  test:
                    services:
                      postgres:
                        image: postgres:18@{OTHER_DIGEST}
                    steps:
                      - uses: actions/checkout@{OTHER_ACTION_SHA}
                """,
            )

            issues, _references = verify_immutable_references.find_issues(root)

            self.assertEqual(2, len(issues))
            self.assertTrue(any("inconsistent SHA" in issue for issue in issues))
            self.assertTrue(any("inconsistent digest" in issue for issue in issues))

    def test_current_repository_has_no_mutable_executable_references(self) -> None:
        repository_root = Path(__file__).resolve().parents[1]

        issues, _references = verify_immutable_references.find_issues(repository_root)

        self.assertEqual([], issues)


if __name__ == "__main__":
    unittest.main()
