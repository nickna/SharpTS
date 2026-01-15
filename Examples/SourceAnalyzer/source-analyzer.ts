// Source Code Analyzer CLI Tool
// A TypeScript CLI tool for SharpTS that analyzes source code files
//
// Usage:
//   dotnet run -- Examples/SourceAnalyzer/source-analyzer.ts
//
// This tool scans the current directory recursively and displays statistics
// about source code files including lines of code and function counts.
//
// Note: Once SharpTS supports script arguments, this tool will accept
// a directory path and --help flag as CLI arguments.

import * as fs from 'fs';
import * as path from 'path';

// ========== Type Definitions ==========

interface FileStats {
    filePath: string;
    fileName: string;
    extension: string;
    totalLines: number;
    nonEmptyLines: number;
    functionCount: number;
}

interface Summary {
    totalFiles: number;
    totalLines: number;
    totalNonEmptyLines: number;
    totalFunctions: number;
}

interface CliArgs {
    directory: string;
    showHelp: boolean;
}

// ========== Constants ==========

const VERSION = '1.0.0';
const EXCLUDED_DIRS: string[] = ['node_modules', '.git', '.vscode', 'dist', 'build', 'obj', 'bin', 'nul'];
const SUPPORTED_EXTENSIONS: string[] = ['.ts', '.tsx', '.js', '.jsx', '.css', '.html', '.json'];

// ========== CLI Functions ==========

function parseArgs(argv: string[]): CliArgs {
    // process.argv contains: [executablePath, scriptPath, ...userArgs]
    // Skip first two elements to get user arguments
    const args = argv.slice(2);

    let showHelp = false;
    let directory = process.cwd();

    for (let i = 0; i < args.length; i = i + 1) {
        const arg = args[i];
        if (arg === '--help' || arg === '-h') {
            showHelp = true;
        } else if (!arg.startsWith('-')) {
            // Non-flag argument is the directory
            directory = arg;
        }
    }

    return { directory: directory, showHelp: showHelp };
}

function showHelp(): void {
    console.log('Source Code Analyzer v' + VERSION);
    console.log('');
    console.log('Analyzes source code files and displays statistics including');
    console.log('lines of code and function counts.');
    console.log('');
    console.log('Usage:');
    console.log('  dotnet run -- Examples/SourceAnalyzer/source-analyzer.ts');
    console.log('');
    console.log('The tool scans the current working directory recursively.');
    console.log('');
    console.log('Supported file extensions:');
    console.log('  .ts, .tsx, .js, .jsx, .css, .html, .json');
    console.log('');
    console.log('Auto-excluded directories:');
    console.log('  node_modules, .git, .vscode, dist, build, obj, bin');
    console.log('');
    console.log('Statistics displayed:');
    console.log('  - Total lines per file');
    console.log('  - Non-empty lines per file');
    console.log('  - Function count (for .ts, .tsx, .js, .jsx files)');
}

// ========== Filtering Functions ==========

function shouldExcludeDirectory(dirName: string): boolean {
    for (let i = 0; i < EXCLUDED_DIRS.length; i = i + 1) {
        if (EXCLUDED_DIRS[i] === dirName) {
            return true;
        }
    }
    return false;
}

function isSupportedFile(fileName: string): boolean {
    const ext = path.extname(fileName).toLowerCase();
    for (let i = 0; i < SUPPORTED_EXTENSIONS.length; i = i + 1) {
        if (SUPPORTED_EXTENSIONS[i] === ext) {
            return true;
        }
    }
    return false;
}

// ========== Analysis Functions ==========

function countFunctions(content: string, extension: string): number {
    // Only count functions for JS/TS files
    if (extension !== '.ts' && extension !== '.tsx' &&
        extension !== '.js' && extension !== '.jsx') {
        return 0;
    }

    let count = 0;
    const lines = content.split('\n');

    for (let i = 0; i < lines.length; i = i + 1) {
        const line = lines[i].trim();

        // Skip comments - use nested if instead of continue (SharpTS for-loop issue)
        const isComment = line.startsWith('//') || line.startsWith('*') || line.startsWith('/*');
        if (!isComment) {
            // Pattern 1: function keyword declarations
            if (line.includes('function ') && line.includes('(')) {
                count = count + 1;
            }
            // Pattern 2: Arrow function assignments
            else if ((line.startsWith('const ') || line.startsWith('let ') ||
                      line.startsWith('var ') || line.startsWith('export const ') ||
                      line.startsWith('export let ')) && line.includes('=>')) {
                count = count + 1;
            }
            // Pattern 3: Class methods (heuristic)
            else if (line.includes('(') && line.includes(')') &&
                     (line.endsWith('{') || line.endsWith(') {'))) {
                // Exclude control flow and common calls
                if (!line.startsWith('if') && !line.startsWith('if ') &&
                    !line.startsWith('while') && !line.startsWith('while ') &&
                    !line.startsWith('for') && !line.startsWith('for ') &&
                    !line.startsWith('switch') && !line.startsWith('switch ') &&
                    !line.startsWith('return') && !line.startsWith('throw') &&
                    !line.includes('console.') && !line.includes('new ') &&
                    !line.includes('super(') && !line.startsWith('} else') &&
                    !line.startsWith('else') && !line.startsWith('catch') &&
                    !line.startsWith('import') && !line.startsWith('export {')) {
                    count = count + 1;
                }
            }
        }
    }

    return count;
}

function analyzeFile(filePath: string): any {
    const content = fs.readFileSync(filePath, 'utf8') as string;
    const lines = content.split('\n');
    const fileName = path.basename(filePath);
    const extension = path.extname(filePath).toLowerCase();

    let nonEmptyLines = 0;
    for (let i = 0; i < lines.length; i = i + 1) {
        if (lines[i].trim().length > 0) {
            nonEmptyLines = nonEmptyLines + 1;
        }
    }

    const functionCount = countFunctions(content, extension);

    return {
        filePath: filePath,
        fileName: fileName,
        extension: extension,
        totalLines: lines.length,
        nonEmptyLines: nonEmptyLines,
        functionCount: functionCount
    };
}

function scanDirectory(dirPath: string): any[] {
    const results: any[] = [];

    // Read directory entries
    const entries = fs.readdirSync(dirPath);

    for (let i = 0; i < entries.length; i = i + 1) {
        const entry = entries[i];

        // Skip Windows reserved names and already excluded dirs
        if (shouldExcludeDirectory(entry)) {
            // Skip this entry entirely (works for both files and dirs with these names)
        } else {
            const fullPath = path.join(dirPath, entry);

            // Check if path exists before trying to stat (handles Windows reserved names)
            if (fs.existsSync(fullPath)) {
                const stat = fs.statSync(fullPath);

                if (stat.isDirectory) {
                    // Recursively scan subdirectory
                    const subResults = scanDirectory(fullPath);
                    for (let j = 0; j < subResults.length; j = j + 1) {
                        results.push(subResults[j]);
                    }
                } else if (stat.isFile) {
                    // Check if it's a supported file type
                    if (isSupportedFile(entry)) {
                        const fileStats = analyzeFile(fullPath);
                        results.push(fileStats);
                    }
                }
            }
        }
    }

    return results;
}

// ========== Output Functions ==========

function formatNumber(num: number): string {
    // Convert number to string, handle potential decimal display
    const str = '' + num;
    if (str.includes('.')) {
        return str.split('.')[0];
    }
    return str;
}

function printTable(stats: any[], summary: any): void {
    // Column widths
    const fileColWidth = 40;
    const extColWidth = 8;
    const linesColWidth = 10;
    const nonEmptyColWidth = 12;
    const funcColWidth = 10;

    // Build separator line
    const separator = '+' + '-'.repeat(fileColWidth + 2) +
                      '+' + '-'.repeat(extColWidth + 2) +
                      '+' + '-'.repeat(linesColWidth + 2) +
                      '+' + '-'.repeat(nonEmptyColWidth + 2) +
                      '+' + '-'.repeat(funcColWidth + 2) + '+';

    // Print header
    console.log(separator);
    console.log('| ' + 'File'.padEnd(fileColWidth) +
                ' | ' + 'Ext'.padEnd(extColWidth) +
                ' | ' + 'Lines'.padStart(linesColWidth) +
                ' | ' + 'Non-Empty'.padStart(nonEmptyColWidth) +
                ' | ' + 'Functions'.padStart(funcColWidth) + ' |');
    console.log(separator);

    // Print rows
    for (let i = 0; i < stats.length; i = i + 1) {
        const s = stats[i];
        // Truncate file name if too long
        let displayName = s.fileName;
        if (displayName.length > fileColWidth) {
            displayName = displayName.substring(0, fileColWidth - 3) + '...';
        }

        console.log('| ' + displayName.padEnd(fileColWidth) +
                    ' | ' + s.extension.padEnd(extColWidth) +
                    ' | ' + formatNumber(s.totalLines).padStart(linesColWidth) +
                    ' | ' + formatNumber(s.nonEmptyLines).padStart(nonEmptyColWidth) +
                    ' | ' + formatNumber(s.functionCount).padStart(funcColWidth) + ' |');
    }

    // Print summary
    console.log(separator);
    const summaryLabel = 'TOTAL (' + formatNumber(summary.totalFiles) + ' files)';
    console.log('| ' + summaryLabel.padEnd(fileColWidth) +
                ' | ' + ''.padEnd(extColWidth) +
                ' | ' + formatNumber(summary.totalLines).padStart(linesColWidth) +
                ' | ' + formatNumber(summary.totalNonEmptyLines).padStart(nonEmptyColWidth) +
                ' | ' + formatNumber(summary.totalFunctions).padStart(funcColWidth) + ' |');
    console.log(separator);
}

function calculateSummary(stats: any[]): any {
    let totalLines = 0;
    let totalNonEmptyLines = 0;
    let totalFunctions = 0;

    for (let i = 0; i < stats.length; i = i + 1) {
        totalLines = totalLines + stats[i].totalLines;
        totalNonEmptyLines = totalNonEmptyLines + stats[i].nonEmptyLines;
        totalFunctions = totalFunctions + stats[i].functionCount;
    }

    return {
        totalFiles: stats.length,
        totalLines: totalLines,
        totalNonEmptyLines: totalNonEmptyLines,
        totalFunctions: totalFunctions
    };
}

// ========== Main Entry Point ==========

function main(): void {
    const args = parseArgs(process.argv);

    if (args.showHelp) {
        showHelp();
        return;
    }

    // Resolve the target directory
    let targetDir: string;
    if (path.isAbsolute(args.directory)) {
        targetDir = args.directory;
    } else {
        targetDir = path.join(process.cwd(), args.directory);
    }

    // Verify directory exists
    if (!fs.existsSync(targetDir)) {
        console.log('Error: Directory not found: ' + targetDir);
        process.exit(1);
    }

    const stat = fs.statSync(targetDir);
    if (!stat.isDirectory) {
        console.log('Error: Path is not a directory: ' + targetDir);
        process.exit(1);
    }

    console.log('Source Code Analyzer v' + VERSION);
    console.log('');
    console.log('Analyzing: ' + targetDir);
    console.log('');

    // Scan and analyze
    const stats = scanDirectory(targetDir);

    if (stats.length === 0) {
        console.log('No supported source files found.');
        return;
    }

    // Calculate summary and display
    const summary = calculateSummary(stats);
    printTable(stats, summary);
}

// Run main
main();
