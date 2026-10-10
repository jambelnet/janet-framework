// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../www/js/transport.js', import.meta.url), 'utf8');
const { httpPageUrl } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
assert.equal(httpPageUrl('https://192.168.178.23:8443/www/#page3', '192.168.178.23\r\n8080\r\nnone', 123), 'http://192.168.178.23:8080/www/?transport=http&v=123#page3');
assert.equal(httpPageUrl('https://home.example:9443/www/?old=1#page0', '*\n9090\nbasic', 456), 'http://home.example:9090/www/?transport=http&v=456#page0');
assert.equal(httpPageUrl('https://[::1]:8443/www/', '::1\n80\nnone', 123), 'http://[::1]/www/?transport=http&v=123');
assert.equal(new URL(httpPageUrl('https://localhost:8443/www/', 'localhost\n\nnone', 123)).port, '8080');
for (const port of ['abc', '0', '65536', '-1', '8080/path']) assert.throws(() => httpPageUrl('https://localhost:8443/www/', 'localhost\n' + port + '\nnone', 123), /HTTP port is invalid/);
console.log('HTTP recovery URL tests passed: saved ports, IPv6, hostname preservation, cache bypass and validation.');
