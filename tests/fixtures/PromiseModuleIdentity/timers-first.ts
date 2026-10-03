import * as timers from 'node:timers/promises';
import * as dns from 'node:dns/promises';
import * as filesystem from 'node:fs/promises';
console.log(typeof filesystem.readFile, typeof dns.lookup, typeof timers.setTimeout);
timers.setTimeout(1, 'ready').then(value => console.log(value));
