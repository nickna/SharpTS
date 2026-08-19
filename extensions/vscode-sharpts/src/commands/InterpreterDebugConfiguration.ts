import * as path from 'path';

export interface InterpreterDebugSettings {
    projectFile?: string;
    additionalReferences?: readonly string[];
}

export interface InterpreterDebugLaunchConfiguration {
    type: 'sharpts-interpreter';
    request: 'launch';
    name: string;
    program: string;
    cwd: string;
    console: 'internalConsole';
    stopOnEntry: false;
    justMyCode: true;
    project?: string;
    references?: string[];
}

/** Builds the launch request shared by the command and its non-VS-Code unit tests. */
export function createInterpreterDebugConfiguration(
    program: string,
    cwd: string,
    settings: InterpreterDebugSettings,
): InterpreterDebugLaunchConfiguration {
    const configuration: InterpreterDebugLaunchConfiguration = {
        type: 'sharpts-interpreter',
        request: 'launch',
        name: `SharpTS Interpreter: ${path.basename(program)}`,
        program,
        cwd,
        console: 'internalConsole',
        stopOnEntry: false,
        justMyCode: true,
    };

    if (settings.projectFile)
        configuration.project = settings.projectFile;
    if (settings.additionalReferences && settings.additionalReferences.length > 0)
        configuration.references = [...settings.additionalReferences];

    return configuration;
}
