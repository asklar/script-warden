import hljs from "highlight.js/lib/core";
import powershell from "highlight.js/lib/languages/powershell";
import dos from "highlight.js/lib/languages/dos";
import vbscript from "highlight.js/lib/languages/vbscript";
import javascript from "highlight.js/lib/languages/javascript";
import xml from "highlight.js/lib/languages/xml";

// Only the interpreters script-warden captures — keeps the bundle small (no CDN; self-contained).
hljs.registerLanguage("powershell", powershell);
hljs.registerLanguage("dos", dos);
hljs.registerLanguage("vbscript", vbscript);
hljs.registerLanguage("javascript", javascript);
hljs.registerLanguage("xml", xml);

// Maps the captured script's ScriptLanguage (as serialized by the backend) to a highlight.js id.
function languageId(language: string): string | null {
    switch ((language || "").toLowerCase()) {
        case "powershell": return "powershell";
        case "batch": return "dos";
        case "vbscript": return "vbscript";
        case "jscript": return "javascript";
        case "windowsscriptfile": return "xml";
        default: return null;
    }
}

function escapeHtml(s: string): string {
    return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

/// Returns highlighted HTML (hljs escapes the code, so it's safe to inject). Falls back to escaped
/// plain text for unknown/unsupported languages.
export function highlightScript(code: string, language: string): string {
    const id = languageId(language);
    if (!id) {
        return escapeHtml(code);
    }
    try {
        return hljs.highlight(code, { language: id, ignoreIllegals: true }).value;
    } catch {
        return escapeHtml(code);
    }
}
