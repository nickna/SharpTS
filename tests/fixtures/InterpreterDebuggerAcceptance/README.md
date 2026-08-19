# Interpreter debugger acceptance fixture

This multi-file program is the repeatable manual acceptance workload documented in
[`docs/debugging-interpreter.md`](../../../docs/debugging-interpreter.md). Copy
`interpreter.launch.json` to `.vscode/launch.json` inside this directory before testing the explicit
launch configuration; `.vscode` is intentionally ignored.

An uninterrupted run prints these lines (worker scheduling may move the final line):

```text
class=2
closure=15
caught=acceptance
finally=ran
args=alpha,beta
env=configured
async=42
yield=2
promise=microtask
timer=callback
worker=42
```
