import {
    createSerialTask,
    getDesktopPlatformInfo,
    getImageDimensions,
    readTextFile,
    renderDrawingToImage,
    renderDrawingToPng,
    showDialog,
    showMessageDialog,
    showOpenFileDialog,
    showSaveFileDialog,
    useDesktopWindow,
    useEffect,
    useRef,
    useState,
    writeTextFileAtomic
} from "@sharpts/gui";
import { existsSync, mkdirSync, unlinkSync, readdirSync } from "fs";
import { basename, dirname, extname, join } from "path";
import { AppAction, AppState } from "./editor-state";
import {
    createDocument,
    createImportedDocument,
    createTextCommand,
    PaintDocument,
    parseProject,
    serializeProject
} from "./document";
import { DocumentSize, NewDocumentDialog } from "./NewDocumentDialog";

export interface DocumentSession {
    open(file?: string): Promise<void>;
    createNew(): Promise<void>;
    saveProject(forceDialog?: boolean): Promise<void>;
    exportPng(): Promise<void>;
    recover(): Promise<void>;
    requestWindowClose(): boolean;
    recent: string[];
    error: string;
    errorDetails: string;
    clearError(): void;
    recoveryPath: string;
    recoveryCount: number;
}

async function readProject(file: string): Promise<PaintDocument> {
    const document = parseProject(await readTextFile(file));
    const checked: string[] = [];
    for (const layer of document.layers)
        for (const command of layer.commands)
            if (command.kind === "image" && checked.indexOf(command.source) < 0) {
                const size = await getImageDimensions(command.source);
                if (size.width > 8192 || size.height > 8192)
                    throw new Error("An embedded image is too large.");
                checked.push(command.source);
            }
    return document;
}

export function useDocumentSession(
    state: AppState,
    dispatch: (action: AppAction) => void,
    requestClose: () => void,
    storageDirectory?: string
): DocumentSession {
    const owner = useDesktopWindow();
    const current = useRef(state);
    current.current = state;
    const workflow = useRef(createSerialTask());
    const closing = useRef<boolean>(false);
    const alive = useRef<boolean>(true);
    const [recent, setRecent] = useState<string[]>([]);
    const [error, setError] = useState<string>("");
    const [errorDetails, setErrorDetails] = useState<string>("");
    const [recoveryCount, setRecoveryCount] = useState<number>(0);
    const sessionId = useRef(String(Date.now()) + "-" + Math.floor(Math.random() * 1000000));
    const settings = useRef(
        storageDirectory ||
            process.env.SHARPAINT_STORAGE_DIRECTORY ||
            join(getDesktopPlatformInfo().localApplicationData, "SharpPaint")
    );
    const recoveryPath = join(settings.current, "recovery-" + sessionId.current + ".sharpaint");
    useEffect(
        () => () => {
            alive.current = false;
        },
        []
    );
    useEffect(() => {
        if (existsSync(settings.current))
            setRecoveryCount(
                (readdirSync(settings.current) as string[]).filter(
                    (file) => file.startsWith("recovery-") && file.endsWith(".sharpaint")
                ).length
            );
        const file = join(settings.current, "recent.json");
        if (existsSync(file))
            void readTextFile(file, 32768)
                .then((text) => {
                    const value: unknown = JSON.parse(text);
                    if (alive.current && Array.isArray(value))
                        setRecent(value.filter((path) => typeof path === "string").slice(0, 8));
                })
                .catch(() => {});
    }, []);
    const remember = async (file: string): Promise<void> => {
        const next = [file, ...recent.filter((path) => path !== file)].slice(0, 8);
        setRecent(next);
        try {
            mkdirSync(settings.current, { recursive: true });
            await writeTextFileAtomic(join(settings.current, "recent.json"), JSON.stringify(next));
        } catch (_) {
            /* A recent-file preference must not invalidate a successful document save. */
        }
    };
    const fail = (operation: string, error: unknown): void => {
        if (!alive.current) return;
        setError(
            operation +
                ". " +
                (operation === "Could not save" || operation === "Could not export"
                    ? "Choose a writable folder and try again."
                    : "Check the file or location and try again.")
        );
        setErrorDetails(String(error));
    };
    const finishEditing = async (): Promise<void> => {
        owner.clearFocus();
        await new Promise<void>((resolve) => setTimeout(() => resolve(), 0));
        const editing = current.current;
        const draft = editing.textDraft;
        if (draft !== null && draft.editing) {
            if (draft.text)
                dispatch({
                    type: "commitText",
                    command: createTextCommand(
                        draft.text,
                        draft,
                        editing.color,
                        editing.fontFamily,
                        editing.textSize,
                        editing.textBold,
                        editing.textItalic
                    )
                });
            else dispatch({ type: "cancelText" });
            await new Promise<void>((resolve) => setTimeout(() => resolve(), 0));
        }
    };
    const save = async (forceDialog: boolean = false): Promise<boolean> => {
        await finishEditing();
        const snapshot = current.current.history.document;
        let file = forceDialog ? null : current.current.filePath;
        if (file === null)
            file = await showSaveFileDialog({
                title: "Save SharpPaint project",
                suggestedFileName: current.current.filePath
                    ? basename(current.current.filePath)
                    : "Untitled.sharpaint",
                defaultExtension: "sharpaint",
                filters: [{ name: "SharpPaint project", patterns: ["*.sharpaint"] }]
            });
        if (file === null) return false;
        if (extname(file).toLowerCase() !== ".sharpaint") file += ".sharpaint";
        await writeTextFileAtomic(file, serializeProject(snapshot));
        if (!alive.current) return false;
        dispatch({ type: "saved", filePath: file, status: "Saved " + basename(file), document: snapshot });
        await remember(file);
        return true;
    };
    const canReplace = async (): Promise<boolean> => {
        await finishEditing();
        if (!current.current.history.dirty) return true;
        const result = await showMessageDialog({
            title: "Save changes?",
            message:
                "Save changes to " +
                (current.current.filePath ? basename(current.current.filePath) : "Untitled") +
                " before continuing?",
            buttons: "saveDiscardCancel"
        });
        if (result === "save") return await save();
        return result === "discard";
    };
    const openPath = async (file: string): Promise<void> => {
        let document: PaintDocument;
        let path: string | null = file;
        if (extname(file).toLowerCase() === ".png") {
            const size = await getImageDimensions(file);
            if (size.width > 8192 || size.height > 8192)
                throw new Error("PNG dimensions must not exceed 8192 × 8192.");
            const image = await renderDrawingToImage({
                width: size.width,
                height: size.height,
                layers: [
                    {
                        isVisible: true,
                        opacity: 1,
                        commands: [
                            {
                                kind: "image",
                                source: file,
                                x: 0,
                                y: 0,
                                width: size.width,
                                height: size.height
                            }
                        ]
                    }
                ]
            });
            document = createImportedDocument(size.width, size.height, image.source);
            path = null;
        } else {
            document = await readProject(file);
        }
        if (!alive.current) return;
        dispatch({ type: "load", document, filePath: path, status: "Opened " + basename(file) });

        await remember(file);
    };
    const run = async (label: string, work: () => Promise<void>): Promise<void> => {
        try {
            await workflow.current.run(work);
        } catch (error) {
            fail(label, error);
        }
    };
    const open = (file?: string): Promise<void> =>
        run("Could not open", async () => {
            if (!(await canReplace())) return;

            if (!file) {
                const files = await showOpenFileDialog({
                    title: "Open project or PNG",
                    filters: [{ name: "SharpPaint projects and images", patterns: ["*.sharpaint", "*.png"] }]
                });
                file = files[0];
            }

            if (file) await openPath(file);
        });
    const createNew = (): Promise<void> =>
        run("Could not create document", async () => {
            const size = await showDialog<DocumentSize>(owner, {
                title: "New document",
                height: 310,
                content: (dialog) => <NewDocumentDialog dialog={dialog} />
            });
            if (size === null || !(await canReplace())) return;
            dispatch({
                type: "load",
                document: createDocument(size.width, size.height),
                filePath: null,
                status: "Created " + size.width + " × " + size.height + " document"
            });
        });
    const saveProject = (forceDialog: boolean = false): Promise<void> =>
        run("Could not save", async () => {
            await save(forceDialog);
        });
    const exportPng = (): Promise<void> =>
        run("Could not export", async () => {
            await finishEditing();
            const snapshot = current.current.history.document;
            let file = await showSaveFileDialog({
                title: "Export PNG",
                suggestedFileName: current.current.filePath
                    ? basename(current.current.filePath, ".sharpaint") + ".png"
                    : "Untitled.png",
                defaultExtension: "png",
                filters: [{ name: "PNG image", patterns: ["*.png"] }]
            });
            if (file === null) return;
            if (extname(file).toLowerCase() !== ".png") file += ".png";
            await renderDrawingToPng(snapshot, file);
            dispatch({ type: "status", status: "Exported " + basename(file) });
        });
    const requestWindowClose = (): boolean => {
        if (closing.current) return false;
        void run("Could not close", async () => {
            if (!(await canReplace())) return;
            await recovery.current;
            if (existsSync(recoveryPath)) unlinkSync(recoveryPath);
            closing.current = true;
            requestClose();
        });
        return true;
    };
    // Recovery is serialized and debounced. Each window has its own file, so concurrent documents cannot overwrite one another.
    const recovery = useRef(Promise.resolve());
    useEffect(() => {
        if (!state.history.dirty) {
            recovery.current = recovery.current
                .then(() => {
                    if (existsSync(recoveryPath)) unlinkSync(recoveryPath);
                })
                .catch(() => {});
            return;
        }
        const snapshot = state.history.document;
        const timer = setTimeout(() => {
            recovery.current = recovery.current.then(async () => {
                try {
                    mkdirSync(settings.current, { recursive: true });
                    await writeTextFileAtomic(recoveryPath, serializeProject(snapshot));
                } catch (error) {
                    if (alive.current)
                        dispatch({
                            type: "status",
                            status: "Recovery copy could not be written: " + String(error)
                        });
                }
            });
        }, 2000);
        return () => clearTimeout(timer);
    }, [state.history.document, state.history.dirty]);
    const recover = (): Promise<void> =>
        run("Could not recover", async () => {
            const files = await showOpenFileDialog({
                title: "Recover a SharpPaint document",
                initialDirectory: settings.current,
                filters: [{ name: "Recovery copies", patterns: ["recovery-*.sharpaint"] }]
            });
            if (!files.length || !(await canReplace())) return;
            const document = await readProject(files[0]);
            if (!alive.current) return;
            dispatch({
                type: "load",
                document,
                filePath: null,
                status: "Recovered document · Save As to keep a new copy",
                recovered: true
            });
        });
    return {
        recover,
        open,
        createNew,
        saveProject,
        exportPng,
        requestWindowClose,
        recent,
        error,
        errorDetails,
        clearError: () => {
            setError("");
            setErrorDetails("");
        },
        recoveryPath,
        recoveryCount
    };
}
