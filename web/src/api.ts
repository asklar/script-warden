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
}

export async function getStatus(): Promise<ServeStatus> {
    const r = await fetch("/api/status");
    if (!r.ok) throw new Error(`status ${r.status}`);
    return r.json();
}

export async function getEvents(): Promise<AuditEvent[]> {
    const r = await fetch("/api/events");
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
