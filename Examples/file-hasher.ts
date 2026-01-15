// File Hasher - Generate checksums for files
// Usage: dotnet run -- examples/file-hasher.ts <filepath>
//
// Demonstrates: crypto (createHash, digest), fs (readFileSync, existsSync, statSync), path (basename, resolve)

import { createHash } from 'crypto';
import * as fs from 'fs';
import * as path from 'path';
import * as process from 'process';

function formatBytes(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB';
    if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
    return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
}

function hashFile(filePath: string, algorithm: string): string {
    const content = fs.readFileSync(filePath, 'utf8');
    return createHash(algorithm).update(content).digest('hex');
}

function main(): void {
    const args = process.argv.slice(2);

    if (args.length === 0) {
        console.log('File Hasher - Generate checksums for files');
        console.log('');
        console.log('Usage: dotnet run -- examples/file-hasher.ts <filepath>');
        console.log('');
        console.log('Supported algorithms: MD5, SHA1, SHA256, SHA384, SHA512');
        return;
    }

    const filePath = path.resolve(args[0]);

    if (!fs.existsSync(filePath)) {
        console.log('Error: File not found - ' + filePath);
        return;
    }

    const stats = fs.statSync(filePath);
    if (stats.isDirectory) {
        console.log('Error: Path is a directory, not a file');
        return;
    }

    console.log('File Hasher Results');
    console.log('===================');
    console.log('');
    console.log('File: ' + path.basename(filePath));
    console.log('Path: ' + filePath);
    console.log('Size: ' + formatBytes(stats.size));
    console.log('');
    console.log('Checksums:');
    console.log('----------');

    const algorithms = ['md5', 'sha1', 'sha256', 'sha512'];

    for (const algo of algorithms) {
        const hash = hashFile(filePath, algo);
        const label = algo.toUpperCase().padEnd(8);
        console.log(label + hash);
    }
}

main();
