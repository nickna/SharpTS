import {
    sampleDrawingPixel,
    startDrawingFill,
    startDrawingImage,
    useEffect,
    useLatestTask,
    useState
} from "@sharpts/gui";
import type { DrawingDocument, DrawingEffect, DrawingTask } from "@sharpts/gui";
import { AppAction, AppState, effectForDialog, effectName } from "./editor-state";
import { PaintLayer } from "./document";

export function useGraphicsSession(state: AppState, dispatch: (action: AppAction) => void) {
    const latest = useLatestTask();
    const [progress, setProgress] = useState<number>(0);
    const [job, setJob] = useState<DrawingTask<unknown> | null>(null);
    const document = state.history.document;
    const selected = document.layers.find((layer) => layer.id === state.selectedLayerId)!;
    const drawing = (layers: readonly PaintLayer[]): DrawingDocument => ({
        width: document.width,
        height: document.height,
        layers
    });
    const isolated = (): DrawingDocument => ({
        width: document.width,
        height: document.height,
        layers: [{ isVisible: true, opacity: 1, commands: selected.commands }]
    });
    useEffect(() => {
        if (job === null) return;
        const timer = setInterval(() => setProgress(job.progress), 100);
        return () => clearInterval(timer);
    }, [job]);
    const run = async <T>(
        task: DrawingTask<T>,
        label: string,
        complete: (result: T) => void
    ): Promise<void> => {
        const id = latest.begin(() => task.cancel());
        setJob(task);
        setProgress(0);
        dispatch({ type: "busy", value: label });
        try {
            const result = await task.result;

            if (latest.isCurrent(id)) complete(result);
        } catch (error) {
            if (latest.isCurrent(id))
                dispatch({ type: "status", status: "Could not finish: " + String(error) });
        } finally {
            if (latest.isCurrent(id)) {
                setJob(null);
                dispatch({ type: "busy", value: null });
            }
        }
    };
    const cancel = (): void => {
        latest.cancel();
        setJob(null);
        dispatch({ type: "busy", value: null });
    };
    const point = (x: number, y: number) => ({
        x: Math.max(0, Math.min(document.width - 1, Math.floor(x))),
        y: Math.max(0, Math.min(document.height - 1, Math.floor(y)))
    });
    const fill = (x: number, y: number): void => {
        void run(
            startDrawingFill(isolated(), {
                ...point(x, y),
                color: state.color,
                tolerance: state.fillTolerance
            }),
            "Filling selected layer…",
            (result) => {
                if (result.changed)
                    dispatch({
                        type: "replaceLayer",
                        layerId: selected.id,
                        source: result.image.source,
                        expectedRevision: state.revision,
                        status: "Filled selected region"
                    });
                else dispatch({ type: "status", status: "Fill made no change" });
            }
        );
    };
    const pick = async (x: number, y: number): Promise<void> => {
        const id = latest.begin();
        try {
            const pixel = await sampleDrawingPixel(drawing(document.layers), point(x, y));
            if (latest.isCurrent(id)) dispatch({ type: "color", color: pixel.color });
        } catch (error) {
            if (latest.isCurrent(id))
                dispatch({ type: "status", status: "Could not sample color: " + String(error) });
        }
    };
    const effect = (value: DrawingEffect, preview: boolean): void => {
        void run(
            startDrawingImage(isolated(), { effects: [value] }),
            preview ? "Rendering effect preview…" : "Applying effect…",
            (image) => {
                if (preview)
                    dispatch({
                        type: "effectPreview",
                        preview: {
                            layerId: selected.id,
                            revision: state.revision,
                            signature: JSON.stringify(value),
                            command: {
                                kind: "image",
                                source: image.source,
                                x: 0,
                                y: 0,
                                width: image.width,
                                height: image.height
                            }
                        }
                    });
                else
                    dispatch({
                        type: "replaceLayer",
                        layerId: selected.id,
                        source: image.source,
                        expectedRevision: state.revision,
                        status: effectName(value) + " applied"
                    });
            }
        );
    };
    const merge = (): void => {
        const index = document.layers.findIndex((layer) => layer.id === selected.id);
        if (index <= 0) return;
        void run(
            startDrawingImage(drawing(document.layers.slice(index - 1, index + 1))),
            "Merging layers…",
            (image) =>
                dispatch({ type: "mergeLayer", source: image.source, expectedRevision: state.revision })
        );
    };
    useEffect(() => {
        if (state.effectDialog === null) return;
        const value = effectForDialog(state.effectDialog);
        const timer = setTimeout(() => effect(value, true), 180);
        return () => {
            clearTimeout(timer);
            cancel();
        };
    }, [state.effectDialog, state.revision, state.selectedLayerId]);
    const apply = (): void => {
        if (state.effectDialog === null) return;
        const value = effectForDialog(state.effectDialog);
        const preview = state.effectPreview;
        if (
            preview &&
            preview.signature === JSON.stringify(value) &&
            preview.revision === state.revision &&
            preview.layerId === selected.id &&
            preview.command.kind === "image"
        ) {
            cancel();
            dispatch({
                type: "replaceLayer",
                layerId: selected.id,
                source: preview.command.source,
                expectedRevision: state.revision,
                status: effectName(value) + " applied"
            });
        } else effect(value, false);
    };
    return { fill, pick, effect, merge, apply, cancel, progress };
}
