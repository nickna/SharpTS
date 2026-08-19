const assert = require('node:assert/strict');
const test = require('node:test');
const {
    createInterpreterDebugConfiguration,
} = require('../../extensions/vscode-sharpts/out/commands/InterpreterDebugConfiguration.js');

test('interpreter command preserves project and reference settings', () => {
    const references = ['one.dll', '../shared/two.dll'];
    const configuration = createInterpreterDebugConfiguration(
        '/workspace/src/app.ts',
        '/workspace',
        {
            projectFile: '/workspace/tsconfig.debug.json',
            additionalReferences: references,
        },
    );

    assert.deepEqual(configuration, {
        type: 'sharpts-interpreter',
        request: 'launch',
        name: 'SharpTS Interpreter: app.ts',
        program: '/workspace/src/app.ts',
        cwd: '/workspace',
        console: 'internalConsole',
        stopOnEntry: false,
        justMyCode: true,
        project: '/workspace/tsconfig.debug.json',
        references,
    });
    assert.notStrictEqual(configuration.references, references);
});

test('interpreter command omits unset optional context', () => {
    const configuration = createInterpreterDebugConfiguration(
        'C:\\workspace\\app.ts',
        'C:\\workspace',
        { projectFile: '', additionalReferences: [] },
    );

    assert.equal(configuration.project, undefined);
    assert.equal(configuration.references, undefined);
});
