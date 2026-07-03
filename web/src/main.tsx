import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider, webLightTheme, webDarkTheme } from "@fluentui/react-components";
import { App, ThemeMode } from "./App";

const STORAGE_KEY = "sw-theme-mode";

function Root() {
    const [mode, setMode] = useState<ThemeMode>(() => {
        const saved = localStorage.getItem(STORAGE_KEY);
        return saved === "light" || saved === "dark" || saved === "system" ? saved : "system";
    });
    const [systemDark, setSystemDark] = useState(
        () => window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false,
    );

    useEffect(() => {
        const mq = window.matchMedia("(prefers-color-scheme: dark)");
        const handler = (e: MediaQueryListEvent) => setSystemDark(e.matches);
        mq.addEventListener("change", handler);
        return () => mq.removeEventListener("change", handler);
    }, []);

    useEffect(() => {
        localStorage.setItem(STORAGE_KEY, mode);
    }, [mode]);

    const isDark = mode === "dark" || (mode === "system" && systemDark);
    const theme = useMemo(() => (isDark ? webDarkTheme : webLightTheme), [isDark]);

    useEffect(() => {
        document.body.style.background = theme.colorNeutralBackground1;
    }, [theme]);

    return (
        <FluentProvider theme={theme} style={{ minHeight: "100vh", background: theme.colorNeutralBackground1 }}>
            <App themeMode={mode} onThemeChange={setMode} />
        </FluentProvider>
    );
}

createRoot(document.getElementById("root")!).render(
    <React.StrictMode>
        <Root />
    </React.StrictMode>,
);
