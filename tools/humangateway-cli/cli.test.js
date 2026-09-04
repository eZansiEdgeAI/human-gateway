const test = require('node:test');
const assert = require('node:assert/strict');
const { checkGatewayId, checkPort, checkUrl, validateConfig, parseArgs } = require('./cli');

test('accepts a gateway ID', () => assert.doesNotThrow(() => checkGatewayId('edge:school-a')));
test('rejects unsafe gateway IDs', () => assert.throws(() => checkGatewayId('../school')));
test('validates ports and URLs', () => { assert.doesNotThrow(() => checkPort('8080')); assert.throws(() => checkPort('70000')); assert.doesNotThrow(() => checkUrl('https://relay.example.test')); assert.throws(() => checkUrl('ftp://relay.example.test')); });
test('allows compose defaults without bootstrap credentials', () => assert.doesNotThrow(() => validateConfig({ mode: 'compose', gatewayId: 'edge:test', edgePort: '8080', relayPort: '5275', dbPort: '127.0.0.1:5433', relayUrl: 'http://127.0.0.1:5275' })));
test('parses setup flags', () => assert.deepEqual(parseArgs(['setup', '--mode', 'edge', '--gateway-id', 'edge:a', '--yes']), { command: 'setup', mode: 'edge', gatewayId: 'edge:a', yes: true }));
