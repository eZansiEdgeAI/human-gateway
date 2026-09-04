#!/usr/bin/env node

const fs = require('node:fs');
const path = require('node:path');
const readline = require('node:readline/promises');
const { stdin, stdout } = require('node:process');
const { spawn, spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '../..');
const CLIENT = path.join(ROOT, 'src', 'HumanGateway.Client');
const WORKFLOW = path.join(ROOT, 'src', 'HumanGateway.Workflow');

function parseArgs(argv) {
  const args = { command: argv[0] || 'setup', yes: false };
  if (args.command === '--help' || args.command === '-h') { args.command = 'setup'; args.help = true; return args; }
  const names = { 'gateway-id': 'gatewayId', 'gateway-name': 'gatewayName', 'edge-port': 'edgePort', 'relay-port': 'relayPort', 'db-port': 'dbPort', 'data-dir': 'dataDir', 'relay-url': 'relayUrl', 'edge-url': 'edgeUrl', 'skip-deps': 'skipdeps', 'skip-build': 'skipbuild', 'skip-tests': 'skiptests', 'no-start': 'nostart' };
  for (let i = 1; i < argv.length; i += 1) {
    const value = argv[i];
    if (value === '--yes' || value === '-y') args.yes = true;
    else if (['--skip-deps', '--skip-build', '--skip-tests', '--no-start'].includes(value)) args[names[value.slice(2)]] = true;
    else if (['--mode', '--gateway-id', '--gateway-name', '--edge-port', '--relay-port', '--db-port', '--data-dir', '--relay-url', '--edge-url'].includes(value)) args[names[value.slice(2)] || value.slice(2)] = argv[++i];
    else if (value === '--help' || value === '-h') args.help = true;
    else throw new Error(`Unknown option: ${value}`);
  }
  return args;
}

function usage() {
  console.log(`HumanGateway setup CLI

Usage:
  npm run setup                         Interactive setup
  npm run setup -- --mode compose -y    Full local stack
  npm run setup -- --mode edge -y       Edge container only
  npm run setup:check                   Check prerequisites
  npm run setup:verify                  Check running services
  npm run setup:status                  Show service status

Options:
  --mode compose|edge|dev  Setup mode
  --gateway-id ID          Edge gateway identity
  --gateway-name NAME      Gateway display name
  --edge-port PORT         Published Edge port
  --relay-port PORT        Published Relay port
  --db-port HOST:PORT      Published PostgreSQL port
  --data-dir PATH          Edge bind-mounted data directory
  --relay-url URL          Relay URL for Edge-only mode
  --skip-deps              Do not install package dependencies
  --skip-build             Do not build project components
  --skip-tests             Do not run tests
  --no-start               Configure and build without starting services
  --yes                    Accept non-destructive defaults
`);
}

function commandExists(command, args = ['--version']) { return spawnSync(command, args, { stdio: 'ignore' }).status === 0; }
function containerRuntimeReady() {
  if (commandExists('docker', ['info'])) return 'Docker daemon';
  if (commandExists('podman', ['info'])) return 'Podman service';
  return false;
}
function composeCommand() {
  if (commandExists('docker', ['info']) && commandExists('docker', ['compose', 'version'])) return ['docker', ['compose']];
  if (commandExists('podman', ['info']) && commandExists('podman-compose')) return ['podman-compose', []];
  throw new Error('Docker Compose v2 or podman-compose is required for compose mode.');
}
function checkPort(value) { const port = Number(value); if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error(`Invalid port: ${value}`); }
function checkGatewayId(value) { if (!/^[a-zA-Z0-9][a-zA-Z0-9:._-]{1,127}$/.test(value)) throw new Error(`Invalid gateway ID '${value}'.`); }
function checkUrl(value, allowEmpty = false) {
  if (!value && allowEmpty) return;
  let parsed; try { parsed = new URL(value); } catch { throw new Error(`Invalid URL: ${value}`); }
  if (!['http:', 'https:'].includes(parsed.protocol)) throw new Error(`URL must use http or https: ${value}`);
}
function existingEnvValue(key) {
  if (process.env[key]) return process.env[key];
  const envPath = path.join(ROOT, '.env');
  if (!fs.existsSync(envPath)) return '';
  const line = fs.readFileSync(envPath, 'utf8').split(/\r?\n/).find((entry) => entry.startsWith(`${key}=`));
  return line ? line.slice(key.length + 1).trim() : '';
}
function nodeVersionAtLeast(major, minor, patch) {
  const [currentMajor, currentMinor, currentPatch] = process.versions.node.split('.').map(Number);
  return currentMajor > major || (currentMajor === major && (currentMinor > minor || (currentMinor === minor && currentPatch >= patch)));
}

function preflight(mode = 'compose') {
  const checks = [];
  const add = (name, ok, detail = '') => checks.push({ name, ok, detail });
  add('repository root', fs.existsSync(path.join(ROOT, 'HumanGateway.slnx')), ROOT);
  add('Node.js >= 22.22.2', nodeVersionAtLeast(22, 22, 2), process.version);
  add('npm', commandExists('npm'));
  add('.NET SDK', commandExists('dotnet'), 'required for Edge and Relay builds');
  if (mode === 'compose' || mode === 'edge') {
    add('container runtime installed', commandExists('docker') || commandExists('podman'), 'Docker or Podman');
    add('container runtime ready', Boolean(containerRuntimeReady()), 'Start Docker Engine or Podman machine/service');
  }
  if (mode === 'compose') add('Compose runtime', (commandExists('docker', ['info']) && commandExists('docker', ['compose', 'version'])) || (commandExists('podman', ['info']) && commandExists('podman-compose')), 'Docker Compose v2 or Podman Compose with a running runtime');
  add('client package', fs.existsSync(path.join(CLIENT, 'package.json')), CLIENT);
  add('workflow package', fs.existsSync(path.join(WORKFLOW, 'package.json')), WORKFLOW);
  return checks;
}

function printChecks(checks) { for (const check of checks) console.log(`${check.ok ? 'OK' : '!!'} ${check.name}${check.detail ? ` (${check.detail})` : ''}`); return checks.every((check) => check.ok); }

async function ask(question, fallback, secret = false) {
  if (secret && !stdin.isTTY) return process.env.HG_SETUP_PASSWORD || fallback;
  if (secret && stdin.isTTY) {
    stdout.write(`${question} `);
    return new Promise((resolve) => {
      let answer = '';
      const onData = (chunk) => {
        const text = chunk.toString();
        if (text === '\n' || text === '\r') {
          stdin.setRawMode(false); stdin.removeListener('data', onData); stdout.write('\n'); resolve(answer || fallback);
        } else if (text === '\u0003') process.exit(130);
        else if (text === '\u007f') answer = answer.slice(0, -1);
        else answer += text;
      };
      stdin.setRawMode(true); stdin.on('data', onData);
    });
  }
  const rl = readline.createInterface({ input: stdin, output: stdout });
  try { return (await rl.question(`${question} [${fallback}] `)) || fallback; } finally { rl.close(); }
}

async function collectConfig(args) {
  const mode = args.mode || (args.yes ? 'compose' : await ask('Setup mode (compose, edge, dev)', 'compose'));
  const defaultEdgePort = mode === 'dev' ? '5187' : '8080';
  if (args.yes) return validateConfig({ mode, gatewayId: args.gatewayId || 'edge:compose', gatewayName: args.gatewayName || 'HumanGateway Edge', edgePort: args.edgePort || defaultEdgePort, relayPort: args.relayPort || '5275', dbPort: args.dbPort || '127.0.0.1:5433', dataDir: args.dataDir || '', relayUrl: args.relayUrl || 'http://127.0.0.1:5275', username: existingEnvValue('HG_EDGE_AUTH_BOOTSTRAP_USERNAME'), password: existingEnvValue('HG_EDGE_AUTH_BOOTSTRAP_PASSWORD'), relayUsername: existingEnvValue('HG_RELAY_AUTH_BOOTSTRAP_USERNAME'), relayPassword: existingEnvValue('HG_RELAY_AUTH_BOOTSTRAP_PASSWORD') });
  const config = {
    mode, gatewayId: args.gatewayId || await ask('Gateway ID', 'edge:compose'), gatewayName: args.gatewayName || await ask('Gateway display name', 'HumanGateway Edge'),
    edgePort: args.edgePort || await ask('Edge port', defaultEdgePort), relayPort: args.relayPort || await ask('Relay port', '5275'), dbPort: args.dbPort || await ask('PostgreSQL host port', '127.0.0.1:5433'),
    dataDir: args.dataDir || await ask('Edge data directory (blank uses named volume)', ''), relayUrl: args.relayUrl || await ask('Relay URL', 'http://127.0.0.1:5275'),
    username: await ask('Edge bootstrap username', 'admin'), password: await ask('Edge bootstrap password', '', true), relayUsername: await ask('Relay bootstrap username', 'admin'), relayPassword: await ask('Relay bootstrap password', '', true),
  };
  return validateConfig(config);
}

function validateConfig(config) {
  if (!['compose', 'edge', 'dev'].includes(config.mode)) throw new Error(`Unsupported setup mode: ${config.mode}`);
  if ((config.mode === 'compose' || config.mode === 'edge') && (!config.username || !config.password)) throw new Error('Bootstrap username and password are required for container setup. Set HG_EDGE_AUTH_BOOTSTRAP_USERNAME and HG_EDGE_AUTH_BOOTSTRAP_PASSWORD or run interactive setup without --yes.');
  if (config.mode === 'compose' && (!config.relayUsername || !config.relayPassword)) throw new Error('Relay bootstrap username and password are required for compose setup. Set HG_RELAY_AUTH_BOOTSTRAP_USERNAME and HG_RELAY_AUTH_BOOTSTRAP_PASSWORD or run interactive setup without --yes.');
  checkGatewayId(config.gatewayId); checkPort(config.edgePort); checkPort(config.relayPort); checkUrl(config.relayUrl, config.mode === 'dev');
  if (config.mode === 'compose' && !/^([^:]+:)?[0-9]+$/.test(config.dbPort)) throw new Error(`Invalid PostgreSQL host port: ${config.dbPort}`);
  return config;
}

function writeEnv(config) {
  const target = path.join(ROOT, '.env');
  if (fs.existsSync(target)) return false;
  const values = { HG_DB_PORT: config.dbPort, HG_RELAY_PORT: config.relayPort, HG_EDGE_PORT: config.edgePort, HG_GATEWAY_ID: config.gatewayId, HG_EDGE_AUTH_BOOTSTRAP_USERNAME: config.username, HG_EDGE_AUTH_BOOTSTRAP_PASSWORD: config.password, HG_RELAY_AUTH_BOOTSTRAP_USERNAME: config.relayUsername, HG_RELAY_AUTH_BOOTSTRAP_PASSWORD: config.relayPassword };
  fs.writeFileSync(target, `# Generated by npm run setup. Keep private.\n${Object.entries(values).map(([key, value]) => `${key}=${String(value).replace(/[\r\n]/g, '')}`).join('\n')}\n`, { mode: 0o600 });
  return true;
}

function run(command, args, options = {}) {
  console.log(`> ${command} ${args.join(' ')}`);
  const result = spawnSync(command, args, { cwd: options.cwd || ROOT, stdio: options.stdio || 'inherit', env: { ...process.env, ...(options.env || {}) } });
  if (result.status !== 0) throw new Error(`${options.label || command} failed with exit code ${result.status}`);
}
function installDependencies() {
  for (const [label, cwd] of [['client', CLIENT], ['workflow', WORKFLOW]]) {
    const command = fs.existsSync(path.join(cwd, 'package-lock.json')) ? 'ci' : 'install';
    run('npm', [command], { cwd, label: `${label} dependency install` });
  }
}
function buildProjects(config, skipTests) {
  run('dotnet', ['build', 'HumanGateway.slnx'], { cwd: ROOT }); run('npm', ['run', 'build'], { cwd: CLIENT }); run('npm', ['run', 'build'], { cwd: WORKFLOW });
  if (!skipTests) { run('npm', ['test'], { cwd: CLIENT }); run('npm', ['test'], { cwd: WORKFLOW }); }
  return config;
}
function start(config) {
  if (config.mode === 'compose') {
    const env = { HG_DB_PORT: config.dbPort, HG_RELAY_PORT: config.relayPort, HG_EDGE_PORT: config.edgePort, HG_GATEWAY_ID: config.gatewayId, HG_EDGE_AUTH_BOOTSTRAP_USERNAME: config.username, HG_EDGE_AUTH_BOOTSTRAP_PASSWORD: config.password, HG_RELAY_AUTH_BOOTSTRAP_USERNAME: config.relayUsername, HG_RELAY_AUTH_BOOTSTRAP_PASSWORD: config.relayPassword };
    const [command, prefix] = composeCommand();
    run(command, [...prefix, 'up', '-d', '--build'], { env });
  }
  if (config.mode === 'edge') { const env = { HG_PORT: String(config.edgePort), HG_GATEWAY_ID: config.gatewayId }; if (config.dataDir) env.HG_DATA_DIR = config.dataDir; if (config.relayUrl) env.HG_RELAY_URL = config.relayUrl; run(path.join(ROOT, 'deployment', 'docker', 'run-edge.sh'), [], { env }); }
  if (config.mode === 'dev') {
    const edgeEnv = {
      ...process.env,
      ASPNETCORE_URLS: `http://127.0.0.1:${config.edgePort}`,
      Edge__GatewayId: config.gatewayId,
      Edge__DataDirectory: path.join(ROOT, 'data', 'dev'),
    };
    const edge = spawn('dotnet', ['run', '--project', path.join(ROOT, 'src', 'HumanGateway.Edge'), '--no-launch-profile'], { cwd: ROOT, env: edgeEnv, detached: true, stdio: 'ignore' });
    edge.unref();

    const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
    const clientEnv = { ...process.env, VITE_EDGE_BASE_URL: `http://127.0.0.1:${config.edgePort}` };
    const client = spawn(npm, ['run', 'dev', '--', '--host', '127.0.0.1'], { cwd: CLIENT, env: clientEnv, detached: true, stdio: 'ignore' });
    client.unref();
  }
}
function request(url) { const result = spawnSync('curl', ['-fsS', '--max-time', '5', url], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }); return result.status === 0 ? result.stdout : null; }
function wait(seconds) { spawnSync(process.execPath, ['-e', `setTimeout(() => process.exit(0), ${seconds * 1000})`], { stdio: 'ignore' }); }
function verify(config = {}) {
  const edge = config.edgeUrl || `http://127.0.0.1:${config.edgePort || (config.mode === 'dev' ? '5187' : '8080')}`; const relay = `http://127.0.0.1:${config.relayPort || '5275'}`;
  const results = [{ name: 'Edge health', url: `${edge}/healthz` }]; if (config.mode === 'compose') results.push({ name: 'Relay health', url: `${relay}/healthz` });
  for (const result of results) {
    result.body = request(result.url);
    for (let attempt = 1; !result.body && attempt < 20; attempt += 1) {
      wait(1);
      result.body = request(result.url);
    }
    console.log(`${result.body ? 'OK' : '!!'} ${result.name}${result.body ? `: ${result.body.trim()}` : ` (${result.url})`}`);
  }
  return results.every((result) => result.body);
}
function status(config = {}) {
  const edgeUrls = config.edgeUrl ? [config.edgeUrl] : ['http://127.0.0.1:8080', 'http://127.0.0.1:5187'];
  const edge = edgeUrls.map((url) => ({ url, health: request(`${url}/healthz`) })).find((result) => result.health);
  const edgeUrl = edge?.url || edgeUrls[0];
  console.log(`Edge:  ${edge?.health || 'not responding'}${edge ? ` (${edgeUrl})` : ''}`);
  console.log(`Relay: ${request('http://127.0.0.1:5275/healthz') || 'not responding'}`);
  console.log(`Sync:  ${request(`${edgeUrl}/sync/status`) || 'not responding'}`);
  console.log(`PWA:   ${request('http://127.0.0.1:5173/') ? 'responding (http://127.0.0.1:5173)' : 'not responding'}`);
}

async function setup(args) {
  const config = await collectConfig(args); if (!printChecks(preflight(config.mode))) throw new Error('Preflight failed. Install the missing prerequisite and rerun setup.');
  if (config.mode === 'compose' && writeEnv(config)) console.log('Wrote .env with mode 0600.'); else if (config.mode === 'compose') console.log('.env already exists; leaving it unchanged.');
  if (!args.skipdeps) installDependencies(); if (!args.skipbuild) buildProjects(config, args.skiptests); if (!args.nostart) start(config);
  if (!args.nostart && !verify({ ...config, edgeUrl: `http://127.0.0.1:${config.edgePort}` })) throw new Error('Services started but health verification failed. Check the startup logs and rerun npm run setup:status.');
  console.log(`\nSetup complete.\nGateway ID: ${config.gatewayId}\nEdge API:  http://127.0.0.1:${config.edgePort}${config.mode === 'compose' ? `\nRelay:     http://127.0.0.1:${config.relayPort}` : ''}${config.mode === 'dev' ? '\nPWA:       http://127.0.0.1:5173' : ''}\nNext: npm run setup:status | npm run setup:verify`);
}

async function main() { try { const args = parseArgs(process.argv.slice(2)); if (args.help) return usage(); if (args.command === 'preflight') return process.exitCode = printChecks(preflight(args.mode || 'compose')) ? 0 : 1; if (args.command === 'verify') return process.exitCode = verify({ ...args, mode: args.mode || 'compose' }) ? 0 : 1; if (args.command === 'status') return status(args); if (args.command === 'setup') return setup(args); throw new Error(`Unknown command: ${args.command}`); } catch (error) { console.error(`\nSetup failed: ${error.message}`); process.exitCode = 1; } }
module.exports = { parseArgs, checkGatewayId, checkPort, checkUrl, preflight, validateConfig, writeEnv, verify };
if (require.main === module) main();
