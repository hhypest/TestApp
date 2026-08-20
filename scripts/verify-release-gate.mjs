import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const requiredWorkflows = ['dotnet', 'security', 'observability', 'performance', 'postman'];

function latestCompleted(runs) {
  return runs
    .filter((run) => run.status === 'completed')
    .sort((left, right) => Date.parse(right.created_at) - Date.parse(left.created_at))[0];
}

export function evaluateReleaseGate(runsByWorkflow) {
  const rows = [];
  const failures = [];

  for (const workflow of requiredWorkflows) {
    const run = latestCompleted(runsByWorkflow[workflow] ?? []);
    if (!run) {
      failures.push(`${workflow}: нет завершённого прогона на релизном коммите`);
      continue;
    }

    const conclusion = run.conclusion ?? '—';
    const runLink = run.html_url ? `[${run.id}](${run.html_url})` : String(run.id);
    rows.push(`| ${workflow} | ${runLink} | ${run.event} | ${run.status}/${conclusion} |`);

    if (conclusion !== 'success') {
      failures.push(`${workflow}: последний завершённый прогон ${run.id} имеет заключение ${conclusion}`);
    }
  }

  return { rows, failures };
}

async function fetchWorkflowRuns(repository, workflow, commitSha, token) {
  const query = new URLSearchParams({ head_sha: commitSha, per_page: '100' });
  const url = `https://api.github.com/repos/${repository}/actions/workflows/${workflow}.yml/runs?${query}`;
  const response = await fetch(url, {
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${token}`,
      'X-GitHub-Api-Version': '2022-11-28',
    },
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`${workflow}: GitHub API вернул ${response.status}: ${body}`);
  }

  const payload = await response.json();
  return payload.workflow_runs ?? [];
}

async function main() {
  const repository = process.env.GITHUB_REPOSITORY;
  const commitSha = process.env.RELEASE_COMMIT_SHA;
  const token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN;
  const evidencePath = process.env.RELEASE_EVIDENCE_PATH || 'artifacts/pipelines.md';

  if (!repository || !commitSha || !token) {
    throw new Error('Нужны GITHUB_REPOSITORY, RELEASE_COMMIT_SHA и GITHUB_TOKEN (или GH_TOKEN).');
  }

  const runsByWorkflow = Object.fromEntries(await Promise.all(
    requiredWorkflows.map(async (workflow) => [
      workflow,
      await fetchWorkflowRuns(repository, workflow, commitSha, token),
    ]),
  ));
  const { rows, failures } = evaluateReleaseGate(runsByWorkflow);

  if (rows.length > 0) {
    console.log(rows.join('\n'));
  }
  if (failures.length > 0) {
    throw new Error(`Release quality gate не пройден:\n- ${failures.join('\n- ')}`);
  }

  await mkdir(path.dirname(evidencePath), { recursive: true });
  await writeFile(evidencePath, `${rows.join('\n')}\n`, 'utf8');
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
