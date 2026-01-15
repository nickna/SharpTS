// File Organizer - Sort files into folders by extension
// Usage: sharpts examples/file-organizer.ts <directory> [--dry-run]
//
// Demonstrates: fs (readdirSync, statSync, mkdirSync, renameSync, existsSync)
//               path (join, extname, basename)

import fs from 'fs';
import path from 'path';
import process from 'process';

// Map extensions to folder names
const EXTENSION_FOLDERS: { [key: string]: string } = {
    // Images
    '.jpg': 'images',
    '.jpeg': 'images',
    '.png': 'images',
    '.gif': 'images',
    '.bmp': 'images',
    '.svg': 'images',
    '.webp': 'images',

    // Documents
    '.pdf': 'documents',
    '.doc': 'documents',
    '.docx': 'documents',
    '.xls': 'documents',
    '.xlsx': 'documents',
    '.ppt': 'documents',
    '.pptx': 'documents',
    '.odt': 'documents',

    // Text
    '.txt': 'text',
    '.md': 'text',
    '.csv': 'text',
    '.json': 'text',
    '.xml': 'text',

    // Code
    '.ts': 'code',
    '.js': 'code',
    '.cs': 'code',
    '.py': 'code',
    '.java': 'code',
    '.cpp': 'code',
    '.c': 'code',
    '.h': 'code',
    '.html': 'code',
    '.css': 'code',

    // Archives
    '.zip': 'archives',
    '.rar': 'archives',
    '.7z': 'archives',
    '.tar': 'archives',
    '.gz': 'archives',

    // Media
    '.mp3': 'audio',
    '.wav': 'audio',
    '.flac': 'audio',
    '.mp4': 'video',
    '.avi': 'video',
    '.mkv': 'video',
    '.mov': 'video'
};

function getFolderForExtension(ext: string): string {
    const lower = ext.toLowerCase();
    const folder = EXTENSION_FOLDERS[lower];
    if (folder) {
        return folder;
    }
    // Default: use extension without dot
    return lower.substring(1) + '-files';
}

function main(): void {
    const args = process.argv.slice(2);

    if (args.length === 0) {
        console.log('File Organizer - Sort files into folders by extension');
        console.log('');
        console.log('Usage: ' + path.basename(process.argv[0]) + ' examples/file-organizer.ts <directory> [--dry-run]');
        console.log('');
        console.log('Options:');
        console.log('  --dry-run    Show what would be done without moving files');
        return;
    }

    const targetDir = path.resolve(args[0]);
    const dryRun = args.indexOf('--dry-run') !== -1;

    if (!fs.existsSync(targetDir)) {
        console.log('Error: Directory not found - ' + targetDir);
        return;
    }

    const stats = fs.statSync(targetDir);
    if (!stats.isDirectory) {
        console.log('Error: Path is not a directory');
        return;
    }

    console.log('File Organizer');
    console.log('==============');
    console.log('Directory: ' + targetDir);
    console.log('Mode: ' + (dryRun ? 'DRY RUN (no changes)' : 'LIVE'));
    console.log('');

    const entries = fs.readdirSync(targetDir);
    let movedCount = 0;
    let skippedCount = 0;

    for (const entry of entries) {
        const fullPath = path.join(targetDir, entry);
        const entryStat = fs.statSync(fullPath);

        // Skip directories
        if (entryStat.isDirectory) {
            continue;
        }

        const ext = path.extname(entry);
        if (ext === '') {
            skippedCount++;
            continue; // Skip files without extension
        }

        const folder = getFolderForExtension(ext);
        const destDir = path.join(targetDir, folder);
        const destPath = path.join(destDir, entry);

        console.log(entry + ' -> ' + folder + '/');

        if (!dryRun) {
            // Create folder if needed
            if (!fs.existsSync(destDir)) {
                fs.mkdirSync(destDir);
            }

            // Move file
            fs.renameSync(fullPath, destPath);
        }

        movedCount++;
    }

    console.log('');
    console.log('Summary:');
    console.log('  Files ' + (dryRun ? 'to move' : 'moved') + ': ' + movedCount);
    console.log('  Files skipped: ' + skippedCount);

    if (dryRun && movedCount > 0) {
        console.log('');
        console.log('Run without --dry-run to apply changes.');
    }
}

main();
