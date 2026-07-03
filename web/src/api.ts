export interface CapturedScript {
    kind: string;
    language: string;
    sha256: string;
    extension: string;
    originalPath?: string;
    sizeBytes: number;
    truncated: boolean;
    note?: string;
}

export interface AuditEvent {
    eventId: string;
    timestampUtc: string;
    hookedImage: string;
    targetPath: string;
    commandLine: string;
    arguments: string[];
    workingDirectory: string;
    user?: string;
    userSid?: string;
    sessionId: number;
    shimProcessId: number;
    childProcessId: number;
    parentProcessName?: string;
    parentProcessId: number;
    parentProcessPath?: string;
    exitCode?: number;
    scripts: CapturedScript[];
    origin: string;
    window: string;
}

export interface RootDto {
    path: string;
    origin: string;
    exists: boolean;
    readable: boolean;
    error?: string;
}

export interface ServeStatus {
    version: string;
    eventCount: number;
    roots: RootDto[];
    images: string[];
    parents: string[];
    windows: string[];
}

export interface EventsPage {
    total: number;
    offset: number;
    limit: number;
    events: AuditEvent[];
}

export interface EventQuery {
    offset?: number;
    limit?: number;
    image?: string;
    origin?: string;
    parent?: string;
    window?: string;
    q?: string;
}

export async function getStatus(): Promise<ServeStatus> {
    const r = await fetch("/api/status");
    if (!r.ok) throw new Error(`status ${r.status}`);
    return r.json();
}

export async function getEvents(query: EventQuery = {}): Promise<EventsPage> {
    const params = new URLSearchParams();
    for (const [k, v] of Object.entries(query)) {
        if (v !== undefined && v !== null && v !== "" && v !== "all") params.set(k, String(v));
    }
    const r = await fetch(`/api/events?${params.toString()}`);
    if (!r.ok) throw new Error(`status ${r.status}`);
    return r.json();
}

export function scriptUrl(s: CapturedScript, origin: string, download = false): string {
    const params = new URLSearchParams({ sha: s.sha256, ext: s.extension, origin });
    if (download) params.set("download", "1");
    return `/api/script?${params.toString()}`;
}

export async function getScript(s: CapturedScript, origin: string): Promise<string> {
    const r = await fetch(scriptUrl(s, origin));
    if (!r.ok) throw new Error(`status ${r.status}`);
    return r.text();
}

export interface ClearResult {
    events: number;
    scripts: number;
    roots: string[];
}

export async function clearLogs(): Promise<ClearResult> {
    const r = await fetch("/api/clear", { method: "POST" });
    if (!r.ok) throw new Error(`status ${r.status}`);
    return r.json();
}

export interface WardenConfig {
    enabled: boolean;
    excludedParents: string[];
    excludedImages: string[];
}

export async function getConfig(): Promise<WardenConfig> {
    const r = await fetch("/api/config");
    if (!r.ok) throw new Error(`status ${r.status}`);
    return r.json();
}

export async function saveConfig(config: WardenConfig): Promise<WardenConfig> {
    const r = await fetch("/api/config", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(config),
    });
    if (!r.ok) throw new Error(`status ${r.status}`);
    return r.json();
}
