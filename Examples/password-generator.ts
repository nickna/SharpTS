// Password Generator - Generate secure random passwords
// Usage: sharpts examples/password-generator.ts [length] [options]
//
// Options:
//   --lowercase, -l    Include lowercase letters (a-z)
//   --uppercase, -u    Include uppercase letters (A-Z)
//   --digits, -d       Include digits (0-9)
//   --symbols, -s      Include symbols (!@#$...)
//   --all, -a          Include all character types
//   --help, -h         Show this help message
//
// If no character options are specified, interactive mode is used.
//
// Demonstrates: crypto (randomBytes, randomInt), readline (questionSync)

import { randomBytes, randomInt } from 'crypto';
import readline from 'readline';
import process from 'process';

const LOWERCASE = 'abcdefghijklmnopqrstuvwxyz';
const UPPERCASE = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
const DIGITS = '0123456789';
const SYMBOLS = '!@#$%^&*()_+-=[]{}|;:,.<>?';

function generatePassword(length: number, charset: string): string {
    let password = '';
    const bytes = randomBytes(length);

    for (let i = 0; i < length; i++) {
        const index = bytes[i] % charset.length;
        password = password + charset.charAt(index);
    }

    return password;
}

function askYesNo(question: string): boolean {
    const answer = readline.questionSync(question + ' (y/n): ');
    return answer.toLowerCase() === 'y' || answer.toLowerCase() === 'yes';
}

function hasFlag(args: string[], longFlag: string, shortFlag: string): boolean {
    return args.indexOf(longFlag) !== -1 || args.indexOf(shortFlag) !== -1;
}

function showHelp(): void {
    console.log('Password Generator - Generate secure random passwords');
    console.log('');
    console.log('Usage: password-generator.ts [length] [options]');
    console.log('');
    console.log('Arguments:');
    console.log('  length             Password length (4-128, default: 16)');
    console.log('');
    console.log('Options:');
    console.log('  --lowercase, -l    Include lowercase letters (a-z)');
    console.log('  --uppercase, -u    Include uppercase letters (A-Z)');
    console.log('  --digits, -d       Include digits (0-9)');
    console.log('  --symbols, -s      Include symbols (!@#$...)');
    console.log('  --all, -a          Include all character types');
    console.log('  --help, -h         Show this help message');
    console.log('');
    console.log('Examples:');
    console.log('  password-generator.ts 16 --all');
    console.log('  password-generator.ts 24 -l -u -d');
    console.log('  password-generator.ts              (interactive mode)');
}

function main(): void {
    const args = process.argv.slice(2);

    // Check for help flag
    if (hasFlag(args, '--help', '-h')) {
        showHelp();
        return;
    }

    console.log('Password Generator');
    console.log('==================');
    console.log('');

    // Check for character set flags
    const hasLowercase = hasFlag(args, '--lowercase', '-l');
    const hasUppercase = hasFlag(args, '--uppercase', '-u');
    const hasDigits = hasFlag(args, '--digits', '-d');
    const hasSymbols = hasFlag(args, '--symbols', '-s');
    const hasAll = hasFlag(args, '--all', '-a');
    const hasAnyCharsetFlag = hasLowercase || hasUppercase || hasDigits || hasSymbols || hasAll;

    // Find length argument (first non-flag argument)
    let length: number = 0;
    let foundLength = false;
    for (const arg of args) {
        if (arg.charAt(0) !== '-') {
            const parsed = parseInt(arg);
            if (!isNaN(parsed)) {
                length = parsed;
                foundLength = true;
                break;
            }
        }
    }

    // Validate or prompt for length
    if (foundLength) {
        if (length < 4 || length > 128) {
            console.log('Error: Length must be between 4 and 128');
            return;
        }
    } else {
        const input = readline.questionSync('Password length (8-32, default 16): ');
        if (input === '') {
            length = 16;
        } else {
            length = parseInt(input);
            if (isNaN(length) || length < 4 || length > 128) {
                console.log('Error: Invalid length');
                return;
            }
        }
    }

    // Build character set
    let charset = '';

    if (hasAnyCharsetFlag) {
        // Non-interactive mode: use flags
        if (hasAll || hasLowercase) {
            charset = charset + LOWERCASE;
        }
        if (hasAll || hasUppercase) {
            charset = charset + UPPERCASE;
        }
        if (hasAll || hasDigits) {
            charset = charset + DIGITS;
        }
        if (hasAll || hasSymbols) {
            charset = charset + SYMBOLS;
        }
    } else {
        // Interactive mode: prompt for each option
        console.log('');
        console.log('Character options:');

        if (askYesNo('Include lowercase (a-z)?')) {
            charset = charset + LOWERCASE;
        }

        if (askYesNo('Include uppercase (A-Z)?')) {
            charset = charset + UPPERCASE;
        }

        if (askYesNo('Include digits (0-9)?')) {
            charset = charset + DIGITS;
        }

        if (askYesNo('Include symbols (!@#$...)?')) {
            charset = charset + SYMBOLS;
        }
    }

    if (charset.length === 0) {
        console.log('');
        console.log('Error: Must select at least one character type');
        return;
    }

    // Generate passwords
    console.log('');
    console.log('Generated Passwords:');
    console.log('--------------------');

    for (let i = 1; i <= 5; i++) {
        const password = generatePassword(length, charset);
        console.log(i + '. ' + password);
    }

    console.log('');
    console.log('Charset size: ' + charset.length + ' characters');
    console.log('Password length: ' + length);

    // Calculate entropy: log2(x) = ln(x) / ln(2)
    const entropy = Math.log(Math.pow(charset.length, length)) / Math.log(2);
    console.log('Entropy: ~' + entropy.toFixed(0) + ' bits');
}

main();
