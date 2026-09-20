import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

function resolveRule(tool, result) {
  const componentReference = result.rule?.toolComponent;
  const component = componentReference
    ? componentReference.index !== undefined
      ? tool.extensions?.[componentReference.index]
      : tool.extensions?.find((item) => item.name === componentReference.name)
    : tool.driver;
  const rules = component?.rules ?? [];
  const id = result.rule?.id ?? result.ruleId;
  const index = result.rule?.index ?? result.ruleIndex;
  const rule = index !== undefined ? rules[index] : rules.find((item) => item.id === id);
  if (!rule || (id !== undefined && rule.id !== id)) {
    throw new Error('Missing or inconsistent CodeQL rule metadata');
  }
  return rule;
}

function describeDiagnostic(item, run) {
  const message = item.message?.text ?? item.message?.markdown ?? item.message?.id;
  const locations = (item.locations ?? [])
    .map((location) => {
      const physical = location.physicalLocation ?? location;
      const artifact = physical.artifactLocation;
      const uri = artifact?.uri ?? run.artifacts?.[artifact?.index]?.location?.uri;
      if (!uri) return '';
      const region = physical.region;
      return `${uri}${region?.startLine ? `:${region.startLine}` : ''}${region?.startColumn ? `:${region.startColumn}` : ''}`;
    })
    .filter(Boolean);
  return (
    [
      item.descriptor?.id ?? item.associatedRule?.id,
      message,
      item.message?.arguments?.length
        ? `Arguments: ${JSON.stringify(item.message.arguments)}`
        : '',
      ...locations,
      item.exception ? `Exception: ${JSON.stringify(item.exception)}` : '',
    ]
      .filter(Boolean)
      .join(' | ') || JSON.stringify(item)
  );
}

export function checkSarif(sarif) {
  if (sarif.version !== '2.1.0' || !sarif.runs?.length) {
    throw new Error('Missing SARIF runs');
  }
  const blocked = [];
  const errors = [];
  for (const run of sarif.runs) {
    if (run.tool?.driver?.name !== 'CodeQL' || !Array.isArray(run.results)) {
      throw new Error('Incomplete CodeQL results');
    }
    for (const invocation of run.invocations ?? []) {
      if (invocation.executionSuccessful === false) {
        errors.push(
          `CodeQL execution failed${invocation.exitCode !== undefined ? ` (exit code ${invocation.exitCode})` : ''}${invocation.exitCodeDescription ? `: ${invocation.exitCodeDescription}` : ''}`,
        );
      }
      for (const notification of [
        ...(invocation.toolExecutionNotifications ?? []),
        ...(invocation.toolConfigurationNotifications ?? []),
      ]) {
        if (notification.level === 'error') {
          errors.push(`CodeQL analysis error: ${describeDiagnostic(notification, run)}`);
        }
      }
    }
    for (const result of run.results) {
      const rule = resolveRule(run.tool, result);
      const level = result.level ?? rule.defaultConfiguration?.level ?? 'warning';
      const severity = rule.properties?.['security-severity'];
      if (severity !== undefined && !Number.isFinite(Number(severity))) {
        throw new Error('Invalid security severity');
      }
      if (level === 'error' || Number(severity) >= 7) {
        const id = result.ruleId ?? rule.id;
        blocked.push(
          result.message || result.locations?.length
            ? `${id} (level=${level}, security-severity=${severity ?? 'n/a'}): ${describeDiagnostic(result, run)}`
            : id,
        );
      }
    }
  }
  if (errors.length) {
    throw new Error(
      [...errors, ...blocked.map((finding) => `CodeQL finding: ${finding}`)].join('\n'),
    );
  }
  return blocked;
}

export function checkDirectory(directory) {
  const files = readdirSync(directory).filter((file) => file.endsWith('.sarif'));
  if (!files.length) throw new Error('No CodeQL SARIF files found');
  const failures = [];
  for (const file of files) {
    try {
      const blocked = checkSarif(JSON.parse(readFileSync(join(directory, file), 'utf8')));
      failures.push(...blocked.map((finding) => `${file}: ${finding}`));
    } catch (error) {
      failures.push(`${file}: ${error.message}`);
    }
  }
  if (failures.length) {
    throw new Error(`CodeQL blocks release:\n${failures.join('\n')}`);
  }
  console.log(`CodeQL gate passed (${files.length} SARIF files)`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    checkDirectory(process.argv[2]);
  } catch (error) {
    // Escape workflow command characters so diagnostic text stays annotation data.
    const message = error.message
      .replaceAll('%', '%25')
      .replaceAll('\r', '%0D')
      .replaceAll('\n', '%0A');
    console.error(
      process.env.GITHUB_ACTIONS === 'true'
        ? `::error title=CodeQL release gate::${message}`
        : error.message,
    );
    process.exitCode = 1;
  }
}
