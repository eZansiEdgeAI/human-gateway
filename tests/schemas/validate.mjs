#!/usr/bin/env node
/**
 * Schema validation runner for the HumanGateway protocol schemas.
 *
 * Registers every schema under its versioned $id, then for each entity runs
 * the fixtures in fixtures/<entity>/:
 *   - valid.json      — a single valid instance, or an array of valid instances
 *   - invalid/*.json  — one file per rejection rule; each must FAIL validation
 *
 * Usage: npm test   (from tests/schemas/)
 */
import { readFileSync, readdirSync, existsSync } from "node:fs";
import { resolve, join, basename } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";

const here = fileURLToPath(new URL(".", import.meta.url));
const schemasDir = resolve(here, "../../schemas");
const fixturesDir = resolve(here, "fixtures");

const SCHEMA_FILES = {
  common: "common.schema.json",
  error: "error.schema.json",
  gateway: "gateway.schema.json",
  user: "user.schema.json",
  participant: "participant.schema.json",
  artifact: "artifact.schema.json",
  message: "message.schema.json",
  delivery: "delivery.schema.json",
  humantask: "humantask.schema.json",
  syncbatch: "syncbatch.schema.json",
};

function readJson(file) {
  return JSON.parse(readFileSync(file, "utf8"));
}

// --- Register all schemas under their $id --------------------------------
const docs = {};
const ajv = new Ajv2020({ allErrors: true, strict: true });
addFormats(ajv);
for (const [name, file] of Object.entries(SCHEMA_FILES)) {
  const doc = readJson(join(schemasDir, file));
  docs[name] = doc;
  ajv.addSchema(doc);
  console.log(`registered  ${file}`);
}

// --- Run fixtures ----------------------------------------------------------
let checks = 0;
let failures = 0;
const fail = (message) => {
  failures += 1;
  console.error(`  FAIL  ${message}`);
};

const entities = Object.keys(SCHEMA_FILES).filter((n) => n !== "common");

for (const entity of entities) {
  const validate = ajv.compile(docs[entity]);
  const entityDir = join(fixturesDir, entity);
  if (!existsSync(entityDir)) {
    fail(`${entity}: fixtures directory missing`);
    continue;
  }

  // valid fixtures
  const validFile = join(entityDir, "valid.json");
  if (!existsSync(validFile)) {
    fail(`${entity}: valid.json missing`);
  } else {
    const valid = readJson(validFile);
    const cases = Array.isArray(valid) ? valid : [valid];
    cases.forEach((instance, i) => {
      checks += 1;
      if (!validate(instance)) {
        fail(`${entity}/valid.json[${i}] — expected VALID but was rejected:`);
        console.error(JSON.stringify(validate.errors, null, 2));
      }
    });
  }

  // invalid fixtures
  const invalidDir = join(entityDir, "invalid");
  if (existsSync(invalidDir)) {
    for (const file of readdirSync(invalidDir).sort()) {
      if (!file.endsWith(".json")) continue;
      checks += 1;
      const instance = readJson(join(invalidDir, file));
      if (validate(instance)) {
        fail(`${entity}/invalid/${file} — expected REJECTION but was accepted`);
      }
    }
  }
}

// --- Summary ---------------------------------------------------------------
console.log("");
console.log(`${checks} fixture checks, ${failures} failure(s)`);
if (failures > 0) {
  process.exitCode = 1;
} else {
  console.log("All schema fixtures pass (Draft 2020-12).");
}
