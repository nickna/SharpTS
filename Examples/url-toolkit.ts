// URL Toolkit - Parse and manipulate URLs
// Usage: dotnet run examples/url-toolkit.ts [url]
//
// Demonstrates: url (parse, resolve), querystring (parse, stringify, escape, unescape)

import { parse, resolve } from 'url';
import querystring from 'querystring';
import process from 'process';
import readline from 'readline';

function printUrlComponents(urlString: string): void {
    const url = parse(urlString);

    console.log('URL Components');
    console.log('--------------');
    console.log('href:     ' + url.href);
    console.log('protocol: ' + (url.protocol || '(none)'));
    console.log('host:     ' + (url.host || '(none)'));
    console.log('hostname: ' + (url.hostname || '(none)'));
    console.log('port:     ' + (url.port || '(default)'));
    console.log('pathname: ' + (url.pathname || '/'));
    console.log('search:   ' + (url.search || '(none)'));
    console.log('hash:     ' + (url.hash || '(none)'));

    // Parse query string if present
    if (url.search) {
        console.log('');
        console.log('Query Parameters');
        console.log('----------------');
        const query = querystring.parse(url.search.substring(1));
        const keys = Object.keys(query);
        for (const key of keys) {
            console.log(key + ' = ' + query[key]);
        }
    }
}

function buildUrlInteractive(): void {
    console.log('');
    console.log('Build URL');
    console.log('---------');

    const protocol = readline.questionSync('Protocol (https): ') || 'https';
    const hostname = readline.questionSync('Hostname: ');

    if (!hostname) {
        console.log('Hostname required');
        return;
    }

    const port = readline.questionSync('Port (optional): ');
    const pathname = readline.questionSync('Path (/): ') || '/';

    // Collect query parameters
    const params: { [key: string]: string } = {};
    console.log('Query parameters (empty key to finish):');

    while (true) {
        const key = readline.questionSync('  Key: ');
        if (key === '') break;
        const value = readline.questionSync('  Value: ');
        params[key] = value;
    }

    // Build URL
    let urlString = protocol + '://' + hostname;
    if (port) {
        urlString = urlString + ':' + port;
    }
    urlString = urlString + pathname;

    if (Object.keys(params).length > 0) {
        urlString = urlString + '?' + querystring.stringify(params);
    }

    console.log('');
    console.log('Built URL: ' + urlString);
    console.log('');
}

function interactiveMode(): void {
    console.log('URL Toolkit - Interactive Mode');
    console.log('==============================');
    console.log('');
    console.log('Commands:');
    console.log('  parse <url>           - Parse and display URL components');
    console.log('  encode <string>       - URL encode a string');
    console.log('  decode <string>       - URL decode a string');
    console.log('  resolve <base> <rel>  - Resolve relative URL');
    console.log('  build                 - Build a URL interactively');
    console.log('  quit                  - Exit');
    console.log('');

    while (true) {
        const input = readline.questionSync('> ');

        if (input === 'quit' || input === 'exit' || input === 'q') {
            break;
        }

        if (input.startsWith('parse ')) {
            const urlStr = input.substring(6).trim();
            console.log('');
            printUrlComponents(urlStr);
            console.log('');
        } else if (input.startsWith('encode ')) {
            const str = input.substring(7).trim();
            const encoded = querystring.escape(str);
            console.log('Encoded: ' + encoded);
        } else if (input.startsWith('decode ')) {
            const encoded = input.substring(7).trim();
            const decoded = querystring.unescape(encoded);
            console.log('Decoded: ' + decoded);
        } else if (input.startsWith('resolve ')) {
            const parts = input.substring(8).trim().split(' ');
            if (parts.length >= 2) {
                const result = resolve(parts[0], parts[1]);
                console.log('Resolved: ' + result);
            } else {
                console.log('Usage: resolve <base-url> <relative-url>');
            }
        } else if (input === 'build') {
            buildUrlInteractive();
        } else if (input !== '') {
            console.log('Unknown command. Type "quit" to exit.');
        }
    }
}

function main(): void {
    const args = process.argv.slice(2);

    if (args.length === 0) {
        interactiveMode();
    } else {
        // Parse URL from argument
        console.log('URL Toolkit');
        console.log('===========');
        console.log('');
        printUrlComponents(args[0]);
    }
}

main();
