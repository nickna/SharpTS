// Password Generator - Generate secure random passwords
// Usage: dotnet run -- examples/password-generator.ts [length]
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

function main(): void {
    console.log('Password Generator');
    console.log('==================');
    console.log('');

    // Get length from args or prompt
    let length: number;
    const args = process.argv.slice(2);

    if (args.length > 0) {
        length = parseInt(args[0]);
        if (isNaN(length) || length < 4 || length > 128) {
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

    // Build character set based on user preferences
    console.log('');
    console.log('Character options:');

    let charset = '';

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
