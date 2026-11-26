// Monaco editor wrapper for the Code Lab. Loads Monaco once from the CDN.
const MONACO = "https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs";
let loading = null;

function loadMonaco() {
    if (window.monaco) return Promise.resolve(window.monaco);
    if (loading) return loading;
    loading = new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = `${MONACO}/loader.js`;
        script.onload = () => {
            window.require.config({ paths: { vs: MONACO } });
            window.require(["vs/editor/editor.main"], () => resolve(window.monaco), reject);
        };
        script.onerror = () => reject(new Error("Could not load the code editor (Monaco) from the CDN."));
        document.head.appendChild(script);
    });
    return loading;
}

function themeName() {
    return document.documentElement.dataset.theme === "light" ? "vs" : "vs-dark";
}

export async function create(element, code, dotnet) {
    const monaco = await loadMonaco();
    const editor = monaco.editor.create(element, {
        value: code,
        language: "csharp",
        theme: themeName(),
        automaticLayout: true,
        minimap: { enabled: false },
        fontSize: 13.5,
        fontFamily: "'JetBrains Mono', 'Cascadia Code', Consolas, monospace",
        fontLigatures: false,
        scrollBeyondLastLine: false,
        renderLineHighlight: "line",
        tabSize: 4,
        glyphMargin: true,
        padding: { top: 10 },
    });
    let decorations = editor.createDecorationsCollection([]);
    let timer = null;
    editor.onDidChangeModelContent(() => {
        clearTimeout(timer);
        timer = setTimeout(() => dotnet.invokeMethodAsync("OnCodeChanged"), 600);
    });
    const observer = new MutationObserver(() => monaco.editor.setTheme(themeName()));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });

    return {
        getValue: () => editor.getValue(),
        setValue: (value) => editor.setValue(value),
        highlight: (line, kind) => {
            if (!line) { decorations.clear(); return; }
            decorations.set([{
                range: new monaco.Range(line, 1, line, 1),
                options: {
                    isWholeLine: true,
                    className: kind === "error" ? "pg-line-error" : kind === "cond-false" ? "pg-line-false" : "pg-line-exec",
                    glyphMarginClassName: kind === "error" ? "pg-glyph-error" : "pg-glyph-exec",
                },
            }]);
            editor.revealLineInCenterIfOutsideViewport(line);
        },
        setMarkers: (markers) => {
            monaco.editor.setModelMarkers(editor.getModel(), "algosharp", markers.map(m => ({
                startLineNumber: m.line, startColumn: m.column, endLineNumber: m.line, endColumn: m.column + 1000,
                message: m.message, severity: m.severity === "error" ? monaco.MarkerSeverity.Error : monaco.MarkerSeverity.Warning,
            })));
        },
        reveal: (line) => { editor.revealLineInCenter(line); editor.setPosition({ lineNumber: line, column: 1 }); editor.focus(); },
        dispose: () => { observer.disconnect(); editor.dispose(); },
    };
}
