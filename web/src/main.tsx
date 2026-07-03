import React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { App } from "./App";

createRoot(document.getElementById("root")!).render(
    <React.StrictMode>
        <FluentProvider theme={webLightTheme}>
            <App />
        </FluentProvider>
    </React.StrictMode>,
);
