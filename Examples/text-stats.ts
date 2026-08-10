#!/usr/bin/env sharpts
// Text Statistics - Summarize a text file from the command line
// Usage: ./Examples/text-stats.ts <file> [--top N] [--min-length N] [--case-sensitive]
//
// Demonstrates: executable TypeScript, command-line options, file I/O, maps, and sorting

import fs from 'fs';
import * as path from 'path';
import process from 'process';

interface Options {
    filePath: string;
    top: number;
    minLength: number;
    caseSensitive: boolean;
}

interface WordCount {
    word: string;
    count: number;
}

function printUsage(): void {
    console.log('Text Statistics - Summarize a text file');
    console.log('');
    console.log('Usage: ./Examples/text-stats.ts <file> [options]');
    console.log('');
    console.log('Options:');
    console.log('  --top N             Show the N most frequent words (default: 10)');
    console.log('  --min-length N      Ignore words shorter than N characters (default: 1)');
    console.log('  --case-sensitive    Count differently-cased words separately');
    console.log('  --help, -h          Show this help');
}

function parsePositiveInteger(value: string | undefined, option: string): number {
    if (value === undefined) {
        throw new Error(option + ' requires a value');
    }

    const parsed = Number(value);
    if (!Number.isInteger(parsed) || parsed < 1) {
        throw new Error(option + ' must be a positive integer');
    }

    return parsed;
}

function parseOptions(args: string[]): Options {
    let filePath = '';
    let top = 10;
    let minLength = 1;
    let caseSensitive = false;

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];

        if (arg === '--top') {
            top = parsePositiveInteger(args[i + 1], '--top');
            i++;
        } else if (arg === '--min-length') {
            minLength = parsePositiveInteger(args[i + 1], '--min-length');
            i++;
        } else if (arg === '--case-sensitive') {
            caseSensitive = true;
        } else if (arg.startsWith('-')) {
            throw new Error('Unknown option: ' + arg);
        } else if (filePath !== '') {
            throw new Error('Only one input file can be analyzed at a time');
        } else {
            filePath = arg;
        }
    }

    if (filePath === '') {
        throw new Error('An input file is required');
    }

    return { filePath, top, minLength, caseSensitive };
}

function countLines(text: string): number {
    if (text.length === 0) return 0;

    const normalized = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
    const lines = normalized.split('\n');
    return normalized.endsWith('\n') ? lines.length - 1 : lines.length;
}

function countWords(text: string, options: Options): WordCount[] {
    const matches = text.match(/[A-Za-z0-9']+/g) || [];
    const counts = new Map<string, number>();

    for (const match of matches) {
        let word = match.replace(/^'+/, '').replace(/'+$/, '');
        if (!options.caseSensitive) word = word.toLowerCase();
        if (word.length < options.minLength) continue;

        counts.set(word, (counts.get(word) || 0) + 1);
    }

    const result: WordCount[] = [];
    for (const [word, count] of counts) {
        result.push({ word, count });
    }

    result.sort((left, right) => {
        if (left.count !== right.count) return right.count - left.count;
        if (left.word < right.word) return -1;
        if (left.word > right.word) return 1;
        return 0;
    });

    return result;
}

function main(args: string[]): number {
    if (args.includes('--help') || args.includes('-h')) {
        printUsage();
        return 0;
    }

    let options: Options;
    try {
        options = parseOptions(args);
    } catch (error) {
        console.error('Error: ' + (error as Error).message);
        console.error("Run './Examples/text-stats.ts --help' for usage.");
        return 1;
    }

    const resolvedPath = path.resolve(options.filePath);
    if (!fs.existsSync(resolvedPath)) {
        console.error('Error: File not found - ' + resolvedPath);
        return 1;
    }
    if (fs.statSync(resolvedPath).isDirectory()) {
        console.error('Error: Path is a directory, not a file - ' + resolvedPath);
        return 1;
    }

    let text: string;
    try {
        text = fs.readFileSync(resolvedPath, 'utf8') as string;
    } catch (error) {
        console.error('Error: Could not read file - ' + (error as Error).message);
        return 1;
    }

    const words = countWords(text, options);
    let totalWords = 0;
    for (const entry of words) totalWords += entry.count;

    console.log('Text Statistics');
    console.log('===============');
    console.log('File:         ' + path.basename(resolvedPath));
    console.log('Lines:        ' + countLines(text));
    console.log('Words:        ' + totalWords);
    console.log('Characters:   ' + text.length);
    console.log('Unique words: ' + words.length);
    console.log('');
    console.log('Top words:');

    const limit = Math.min(options.top, words.length);
    if (limit === 0) {
        console.log('  (none)');
    } else {
        for (let i = 0; i < limit; i++) {
            console.log('  ' + (i + 1) + '. ' + words[i].word + ' - ' + words[i].count);
        }
    }

    return 0;
}

process.exit(main(process.argv.slice(2)));
