// Computational Benchmarks - TypeScript Implementations
// Contains ONLY function definitions (no top-level execution)
// These functions will be compiled to .NET IL and invoked via reflection

// Fibonacci (recursive) - Tests function call overhead
function fibonacci(n: number): number {
    if (n <= 1) return n;
    return fibonacci(n - 1) + fibonacci(n - 2);
}

// Factorial (iterative) - Tests loop performance
function factorial(n: number): number {
    let result: number = 1;
    for (let i: number = 2; i <= n; i++) {
        result = result * i;
    }
    return result;
}

// Count Primes (Sieve of Eratosthenes) - Tests array allocation and nested loops
function countPrimes(n: number): number {
    if (n <= 2) return 0;

    let isPrime: boolean[] = [];
    for (let i: number = 0; i < n; i++) {
        isPrime.push(true);
    }
    isPrime[0] = false;
    isPrime[1] = false;

    for (let i: number = 2; i * i < n; i++) {
        if (isPrime[i]) {
            for (let j: number = i * i; j < n; j = j + i) {
                isPrime[j] = false;
            }
        }
    }

    let count: number = 0;
    for (let i: number = 0; i < n; i++) {
        if (isPrime[i]) count++;
    }
    return count;
}
