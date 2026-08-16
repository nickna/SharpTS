// npm Package: uuid — consume a real package from npm
// Setup (one-time):  cd samples/NpmUuid && npm install
// Usage:
//   sharpts samples/NpmUuid/npm-uuid.ts            (interpreted)
//   sharpts --compile samples/NpmUuid/npm-uuid.ts  (compiled)
//
// Demonstrates: resolving a real npm package from node_modules via named ESM
//               imports, calling into its functions, using its validators,
//               and round-tripping string IDs through NIL and parse/stringify.

import { v4, validate, version, NIL, parse, stringify } from 'uuid';

function section(title: string) {
    console.log('');
    console.log(title);
    console.log('-'.repeat(title.length));
}

console.log('npm Package: uuid');
console.log('=================');

section('1. Generate UUIDs (v4, random)');
const ids = [v4(), v4(), v4()];
for (let i = 0; i < ids.length; i++) {
    console.log('  ' + ids[i]);
}

section('2. Validate an ID');
const sample = ids[0];
console.log('  id:        ' + sample);
console.log('  validate:  ' + validate(sample));
console.log('  version:   ' + version(sample));
console.log('  garbage:   validate("not-a-uuid") = ' + validate('not-a-uuid'));

section('3. Constants');
console.log('  NIL:  ' + NIL);
console.log('  NIL validates as UUID: ' + validate(NIL));

section('4. parse() to raw bytes');
const bytes = parse(sample);
console.log('  bytes[0..3]:    ' + bytes[0] + ', ' + bytes[1] + ', ' + bytes[2] + ', ' + bytes[3]);
console.log('  bytes.length:   ' + bytes.length);

section('5. parse() → stringify() round-trip');
const roundTripped = stringify(parse(sample));
console.log('  original:     ' + sample);
console.log('  round-trip:   ' + roundTripped);
console.log('  matches:      ' + (sample === roundTripped));

section('6. Uniqueness check (1000 generations)');
const seen: { [key: string]: boolean } = {};
let collisions = 0;
for (let i = 0; i < 1000; i++) {
    const id = v4();
    if (seen[id]) collisions++;
    seen[id] = true;
}
console.log('  collisions in 1000: ' + collisions);
