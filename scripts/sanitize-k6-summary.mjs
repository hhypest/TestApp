#!/usr/bin/env node

import { mkdir, readFile, rename, unlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { pathToFileURL } from 'node:url';

const JWT_PATTERN = /\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\b/;
const BEARER_PATTERN = /\bBearer\s+[A-Za-z0-9._~+/-]+=*/i;
const PRIVATE_KEY_PATTERN = /-----BEGIN [A-Z ]*PRIVATE KEY-----/;
const SENSITIVE_KEY = /^(?:authorization|password|secret|client_secret|access_token|refresh_token|id_token|token)$/i;

function sensitiveValuePath(value, currentPath = '$') {
  if (typeof value === 'string') {
    if (JWT_PATTERN.test(value) || BEARER_PATTERN.test(value) || PRIVATE_KEY_PATTERN.test(value)) {
      return currentPath;
    }
    return null;
  }

  if (Array.isArray(value)) {
    for (let index = 0; index < value.length; index += 1) {
      const found = sensitiveValuePath(value[index], `${currentPath}[${index}]`);
      if (found !== null) {
        return found;
      }
    }
    return null;
  }

  if (value === null || typeof value !== 'object') {
    return null;
  }

  for (const [key, child] of Object.entries(value)) {
    const childPath = `${currentPath}.${key}`;
    if (SENSITIVE_KEY.test(key) && typeof child === 'string' && child.length > 0) {
      return childPath;
    }
    const found = sensitiveValuePath(child, childPath);
    if (found !== null) {
      return found;
    }
  }

  return null;
}

export function sanitizeSummary(summary) {
  if (summary === null || typeof summary !== 'object' || Array.isArray(summary)) {
    throw new TypeError('k6 summary must be a JSON object');
  }

  const sanitized = { ...summary };
  delete sanitized.setup_data;
  delete sanitized.setupData;

  const sensitivePath = sensitiveValuePath(sanitized);
  if (sensitivePath !== null) {
    throw new Error(`k6 summary still contains a sensitive value at ${sensitivePath}`);
  }

  return sanitized;
}

export async function sanitizeSummaryFile(inputPath, outputPath) {
  const resolvedInput = path.resolve(inputPath);
  const resolvedOutput = path.resolve(outputPath);
  if (resolvedInput === resolvedOutput) {
    throw new Error('raw and sanitized k6 summary paths must be different');
  }

  const raw = await readFile(resolvedInput, 'utf8');
  let summary;
  try {
    summary = JSON.parse(raw);
  } catch (error) {
    throw new Error(`raw k6 summary is not valid JSON: ${error.message}`, { cause: error });
  }

  const sanitized = sanitizeSummary(summary);
  await mkdir(path.dirname(resolvedOutput), { recursive: true });

  const temporaryPath = `${resolvedOutput}.${process.pid}.${Date.now()}.tmp`;
  try {
    await writeFile(temporaryPath, `${JSON.stringify(sanitized, null, 2)}\n`, {
      encoding: 'utf8',
      flag: 'wx',
      mode: 0o600,
    });
    await rename(temporaryPath, resolvedOutput);
  } catch (error) {
    await unlink(temporaryPath).catch(() => {});
    throw error;
  }

  await unlink(resolvedInput);
  return sanitized;
}

async function main(argv) {
  if (argv.length !== 2) {
    throw new Error('Usage: sanitize-k6-summary.mjs <raw-summary.json> <safe-summary.json>');
  }

  const [inputPath, outputPath] = argv;
  await sanitizeSummaryFile(inputPath, outputPath);
  console.log(`Sanitized k6 summary written to ${outputPath}; raw setup_data removed`);
}

const invokedPath = process.argv[1] ? pathToFileURL(path.resolve(process.argv[1])).href : null;
if (invokedPath === import.meta.url) {
  main(process.argv.slice(2)).catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
