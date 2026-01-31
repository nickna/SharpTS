// Web Server Example - HTTP server with routing, static pages, and JSON API
// Usage: sharpts Examples/web-server.ts [port]
//        sharpts Examples/web-server.ts --help
//
// Demonstrates: http module (createServer, request/response handling),
//               url parsing, querystring, routing, JSON API, static HTML

import * as http from 'http';
import { parse } from 'url';
import querystring from 'querystring';
import process from 'process';

// ============================================================================
// Helper Functions
// ============================================================================

/**
 * Escapes HTML special characters to prevent XSS.
 */
function escapeHtml(text: string): string {
    return text
        .split('&').join('&amp;')
        .split('<').join('&lt;')
        .split('>').join('&gt;')
        .split('"').join('&quot;')
        .split("'").join('&#39;');
}

/**
 * Sends an HTML response.
 */
function sendHtml(res: any, statusCode: number, html: string): void {
    res.writeHead(statusCode, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end(html);
}

/**
 * Sends a JSON response.
 */
function sendJson(res: any, statusCode: number, data: any): void {
    res.writeHead(statusCode, { 'Content-Type': 'application/json' });
    const json = JSON.stringify(data);
    res.end(json);
}

/**
 * Formats a date as ISO string with readable local time.
 */
function formatDateTime(date: Date): string {
    return date.toISOString();
}

/**
 * Gets the current timestamp in ISO format.
 */
function getCurrentTimestamp(): string {
    const now = new Date();
    return now.toISOString();
}

// ============================================================================
// HTML Page Templates
// ============================================================================

function getHomePage(): string {
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>SharpTS Web Server</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 800px; margin: 0 auto; padding: 20px; line-height: 1.6; }
        h1 { color: #333; border-bottom: 2px solid #007acc; padding-bottom: 10px; }
        nav { background: #f5f5f5; padding: 15px; border-radius: 8px; margin: 20px 0; }
        nav a { display: inline-block; margin-right: 20px; color: #007acc; text-decoration: none; }
        nav a:hover { text-decoration: underline; }
        .section { margin: 30px 0; }
        h2 { color: #555; }
        code { background: #f0f0f0; padding: 2px 6px; border-radius: 4px; font-family: 'Consolas', 'Monaco', monospace; }
        pre { background: #282c34; color: #abb2bf; padding: 15px; border-radius: 8px; overflow-x: auto; }
        ul { padding-left: 20px; }
        li { margin: 8px 0; }
    </style>
</head>
<body>
    <h1>Welcome to SharpTS Web Server</h1>

    <nav>
        <strong>Navigation:</strong>
        <a href="/">Home</a>
        <a href="/about">About</a>
        <a href="/api/time">API: Time</a>
        <a href="/api/echo?hello=world">API: Echo</a>
        <a href="/api/greet/Developer">API: Greet</a>
    </nav>

    <div class="section">
        <h2>About This Server</h2>
        <p>This is a demonstration web server built with <strong>SharpTS</strong>,
        a TypeScript interpreter and compiler for .NET. It showcases HTTP server
        capabilities including routing, static HTML pages, and dynamic JSON APIs.</p>
    </div>

    <div class="section">
        <h2>Available Routes</h2>
        <ul>
            <li><code>GET /</code> - This home page</li>
            <li><code>GET /about</code> - About page with server info</li>
            <li><code>GET /api/time</code> - Current server timestamp (JSON)</li>
            <li><code>GET /api/echo</code> - Echo request information (JSON)</li>
            <li><code>GET /api/greet/:name</code> - Personalized greeting (JSON)</li>
        </ul>
    </div>

    <div class="section">
        <h2>Try the API</h2>
        <pre>curl http://localhost:3000/api/time
curl http://localhost:3000/api/echo?name=test
curl http://localhost:3000/api/greet/World</pre>
    </div>
</body>
</html>`;
}

function getAboutPage(): string {
    const timestamp = getCurrentTimestamp();
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>About - SharpTS Web Server</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 800px; margin: 0 auto; padding: 20px; line-height: 1.6; }
        h1 { color: #333; border-bottom: 2px solid #007acc; padding-bottom: 10px; }
        nav { background: #f5f5f5; padding: 15px; border-radius: 8px; margin: 20px 0; }
        nav a { display: inline-block; margin-right: 20px; color: #007acc; text-decoration: none; }
        nav a:hover { text-decoration: underline; }
        .info-box { background: #e7f3ff; border-left: 4px solid #007acc; padding: 15px; margin: 20px 0; }
        table { border-collapse: collapse; width: 100%; margin: 20px 0; }
        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }
        th { background: #f5f5f5; }
        code { background: #f0f0f0; padding: 2px 6px; border-radius: 4px; }
    </style>
</head>
<body>
    <h1>About SharpTS Web Server</h1>

    <nav>
        <a href="/">Home</a>
        <a href="/about">About</a>
        <a href="/api/time">API: Time</a>
    </nav>

    <div class="info-box">
        <p><strong>SharpTS</strong> is a TypeScript interpreter and compiler implemented in C#
        using .NET. It supports both tree-walking interpretation and ahead-of-time
        compilation to .NET IL.</p>
    </div>

    <h2>Server Information</h2>
    <table>
        <tr>
            <th>Property</th>
            <th>Value</th>
        </tr>
        <tr>
            <td>Runtime</td>
            <td>SharpTS</td>
        </tr>
        <tr>
            <td>Server Started</td>
            <td>${timestamp}</td>
        </tr>
        <tr>
            <td>HTTP Module</td>
            <td>Built-in http module (Node.js compatible)</td>
        </tr>
    </table>

    <h2>Features Demonstrated</h2>
    <ul>
        <li><code>http.createServer()</code> - Creating an HTTP server</li>
        <li>Server events: <code>on('listening')</code>, <code>on('error')</code></li>
        <li>Request properties: <code>method</code>, <code>url</code>, <code>headers</code></li>
        <li>Response methods: <code>writeHead()</code>, <code>end()</code></li>
        <li>URL parsing with <code>url.parse()</code></li>
        <li>Query string parsing with <code>querystring.parse()</code></li>
    </ul>

    <p><a href="/">← Back to Home</a></p>
</body>
</html>`;
}

function getNotFoundPage(path: string): string {
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>404 Not Found - SharpTS Web Server</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 800px; margin: 0 auto; padding: 20px; line-height: 1.6; text-align: center; }
        h1 { color: #e74c3c; font-size: 72px; margin: 40px 0 10px 0; }
        h2 { color: #333; font-weight: normal; }
        .path { background: #f0f0f0; padding: 10px 20px; border-radius: 8px; display: inline-block; margin: 20px 0; font-family: monospace; }
        a { color: #007acc; }
    </style>
</head>
<body>
    <h1>404</h1>
    <h2>Page Not Found</h2>
    <p class="path">${escapeHtml(path)}</p>
    <p>The requested resource could not be found on this server.</p>
    <p><a href="/">← Return to Home</a></p>
</body>
</html>`;
}

// ============================================================================
// Route Handlers
// ============================================================================

function handleHome(req: any, res: any): void {
    sendHtml(res, 200, getHomePage());
}

function handleAbout(req: any, res: any): void {
    sendHtml(res, 200, getAboutPage());
}

function handleApiTime(req: any, res: any): void {
    const now = new Date();
    const isoString = now.toISOString();
    const parts = isoString.split('T');
    const datePart = parts[0];
    const timeParts = parts[1].split('.');
    const timePart = timeParts[0];

    const data = {
        success: true,
        timestamp: isoString,
        unix: now.getTime(),
        date: datePart,
        time: timePart
    };
    sendJson(res, 200, data);
}

function handleApiEcho(req: any, res: any, parsedUrl: any, query: any): void {
    const headers = req.headers;
    const headerKeys = Object.keys(headers);
    const headerObj: { [key: string]: string } = {};
    for (const key of headerKeys) {
        headerObj[key] = headers[key];
    }

    sendJson(res, 200, {
        success: true,
        request: {
            method: req.method,
            url: req.url,
            path: parsedUrl.pathname,
            query: query,
            headers: headerObj
        },
        socket: {
            remoteAddress: req.socket.remoteAddress,
            remotePort: req.socket.remotePort
        }
    });
}

function handleApiGreet(req: any, res: any, name: string): void {
    const safeName = escapeHtml(name);
    const greetings = [
        'Hello',
        'Welcome',
        'Greetings',
        'Hi there',
        'Good day'
    ];

    // Simple hash to pick a consistent greeting for each name
    let hash = 0;
    for (let i = 0; i < name.length; i = i + 1) {
        hash = hash + name.charCodeAt(i);
    }
    const greeting = greetings[hash % greetings.length];

    sendJson(res, 200, {
        success: true,
        greeting: greeting + ', ' + safeName + '!',
        name: safeName,
        timestamp: getCurrentTimestamp()
    });
}

function handleNotFound(req: any, res: any, path: string): void {
    sendHtml(res, 404, getNotFoundPage(path));
}

// ============================================================================
// Main Router
// ============================================================================

function handleRequest(req: any, res: any): void {
    const method = req.method;
    const url = req.url;

    // Log the request
    console.log(method + ' ' + url);

    // Parse the URL
    const parsedUrl = parse(url);
    let pathname = '/';
    if (parsedUrl.pathname) {
        pathname = parsedUrl.pathname;
    }

    // Parse query string
    let query: { [key: string]: string } = {};
    if (parsedUrl.search) {
        query = querystring.parse(parsedUrl.search.substring(1));
    }

    // Route the request
    if (pathname === '/' && method === 'GET') {
        handleHome(req, res);
    } else if (pathname === '/about' && method === 'GET') {
        handleAbout(req, res);
    } else if (pathname === '/api/time' && method === 'GET') {
        handleApiTime(req, res);
    } else if (pathname === '/api/echo' && method === 'GET') {
        handleApiEcho(req, res, parsedUrl, query);
    } else if (pathname.startsWith('/api/greet/') && method === 'GET') {
        const name = pathname.substring(11); // Remove '/api/greet/'
        if (name.length > 0) {
            handleApiGreet(req, res, name);
        } else {
            handleApiGreet(req, res, 'World');
        }
    } else {
        handleNotFound(req, res, pathname);
    }
}

// ============================================================================
// Entry Point
// ============================================================================

function showHelp(): void {
    console.log('SharpTS Web Server Example');
    console.log('==========================');
    console.log('');
    console.log('A demonstration HTTP server with routing, static HTML pages,');
    console.log('and dynamic JSON API endpoints.');
    console.log('');
    console.log('Usage:');
    console.log('  sharpts Examples/web-server.ts [port]');
    console.log('  sharpts Examples/web-server.ts --help');
    console.log('');
    console.log('Arguments:');
    console.log('  port     Port number to listen on (default: 3000)');
    console.log('  --help   Show this help message');
    console.log('');
    console.log('Routes:');
    console.log('  GET /              Home page with navigation');
    console.log('  GET /about         About page with server info');
    console.log('  GET /api/time      Current server timestamp (JSON)');
    console.log('  GET /api/echo      Echo request info (JSON)');
    console.log('  GET /api/greet/:n  Personalized greeting (JSON)');
    console.log('');
    console.log('Examples:');
    console.log('  sharpts Examples/web-server.ts');
    console.log('  sharpts Examples/web-server.ts 8080');
    console.log('');
    console.log('Then open http://localhost:3000 in your browser.');
}

function main(): void {
    const args = process.argv.slice(2);

    // Check for help flag
    if (args.length > 0 && (args[0] === '--help' || args[0] === '-h')) {
        showHelp();
        return;
    }

    // Parse port number
    let port = 3000;
    if (args.length > 0) {
        const parsedPort = parseInt(args[0]);
        if (parsedPort > 0 && parsedPort < 65536) {
            port = parsedPort;
        } else {
            console.log('Invalid port number: ' + args[0]);
            console.log('Using default port 3000');
        }
    }

    // Create the server
    const server = http.createServer(handleRequest);

    // Register event handlers
    server.on('listening', () => {
        console.log('');
        console.log('SharpTS Web Server');
        console.log('==================');
        console.log('Server running at http://localhost:' + port + '/');
        console.log('');
        console.log('Available routes:');
        console.log('  GET /              - Home page');
        console.log('  GET /about         - About page');
        console.log('  GET /api/time      - Current timestamp (JSON)');
        console.log('  GET /api/echo      - Echo request info (JSON)');
        console.log('  GET /api/greet/:n  - Personalized greeting (JSON)');
        console.log('');
        console.log('Press Ctrl+C to stop the server.');
        console.log('');
    });

    server.on('error', (err: any) => {
        console.log('Server error: ' + err.message);
    });

    // Start listening
    server.listen(port);
}

main();
