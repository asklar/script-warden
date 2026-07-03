import { useEffect, useMemo, useState } from "react";
import {
    makeStyles,
    tokens,
    Title2,
    Subtitle2,
    Caption1,
    Text,
    Input,
    Dropdown,
    Option,
    Button,
    Badge,
    Spinner,
    Link,
    Divider,
    Table,
    TableHeader,
    TableRow,
    TableHeaderCell,
    TableBody,
    TableCell,
    TableCellLayout,
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogContent,
    DialogActions,
    MessageBar,
    MessageBarBody,
    MessageBarTitle,
} from "@fluentui/react-components";
import {
    ArrowDownloadRegular,
    DocumentTextRegular,
    ArrowClockwiseRegular,
} from "@fluentui/react-icons";
import {
    AuditEvent,
    CapturedScript,
    RootDto,
    ServeStatus,
    getEvents,
    getScript,
    getStatus,
    scriptUrl,
} from "./api";

const useStyles = makeStyles({
    root: {
        maxWidth: "1200px",
        margin: "0 auto",
        padding: "24px",
        display: "flex",
        flexDirection: "column",
        gap: "12px",
    },
    headerRow: { display: "flex", alignItems: "center", justifyContent: "space-between" },
    toolbar: { display: "flex", gap: "12px", alignItems: "flex-end", flexWrap: "wrap" },
    grow: { flex: 1 },
    search: { minWidth: "280px" },
    clickable: { cursor: "pointer" },
    mono: {
        fontFamily: tokens.fontFamilyMonospace,
        whiteSpace: "pre-wrap",
        wordBreak: "break-all",
        background: tokens.colorNeutralBackground3,
        padding: "8px",
        borderRadius: tokens.borderRadiusMedium,
        margin: 0,
    },
    scriptView: {
        fontFamily: tokens.fontFamilyMonospace,
        fontSize: tokens.fontSizeBase200,
        whiteSpace: "pre-wrap",
        wordBreak: "break-word",
        background: tokens.colorNeutralBackground3,
        padding: "12px",
        borderRadius: tokens.borderRadiusMedium,
        maxHeight: "320px",
        overflow: "auto",
        margin: 0,
    },
    fieldGrid: {
        display: "grid",
        gridTemplateColumns: "140px 1fr",
        rowGap: "4px",
        columnGap: "12px",
        alignItems: "start",
    },
    label: { color: tokens.colorNeutralForeground3 },
    scriptCard: {
        border: `1px solid ${tokens.colorNeutralStroke2}`,
        borderRadius: tokens.borderRadiusMedium,
        padding: "12px",
        marginTop: "8px",
        display: "flex",
        flexDirection: "column",
        gap: "6px",
    },
    scriptActions: { display: "flex", gap: "8px", alignItems: "center" },
});

const ALL = "all";

function fmtTime(iso: string): string {
    if (!iso) return "";
    return iso.replace("T", " ").slice(0, 19) + " UTC";
}

function fmtSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function App() {
    const styles = useStyles();
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [events, setEvents] = useState<AuditEvent[]>([]);
    const [status, setStatus] = useState<ServeStatus | null>(null);
    const [search, setSearch] = useState("");
    const [imageFilter, setImageFilter] = useState(ALL);
    const [originFilter, setOriginFilter] = useState(ALL);
    const [selected, setSelected] = useState<AuditEvent | null>(null);

    async function load() {
        setLoading(true);
        setError(null);
        try {
            const [s, e] = await Promise.all([getStatus(), getEvents()]);
            setStatus(s);
            setEvents(e);
        } catch (err) {
            setError(err instanceof Error ? err.message : String(err));
        } finally {
            setLoading(false);
        }
    }

    useEffect(() => {
        void load();
    }, []);

    const images = useMemo(() => {
        const set = new Set(events.map((e) => e.hookedImage).filter(Boolean));
        return [ALL, ...Array.from(set).sort()];
    }, [events]);

    const filtered = useMemo(() => {
        const q = search.trim().toLowerCase();
        return events.filter((e) => {
            if (imageFilter !== ALL && e.hookedImage !== imageFilter) return false;
            if (originFilter !== ALL && e.origin !== originFilter) return false;
            if (!q) return true;
            const hay = [
                e.hookedImage,
                e.commandLine,
                e.user,
                e.parentProcessName,
                e.parentProcessPath,
                e.origin,
                ...e.scripts.map((s) => `${s.originalPath ?? ""} ${s.kind} ${s.language} ${s.note ?? ""}`),
            ]
                .join(" ")
                .toLowerCase();
            return hay.includes(q);
        });
    }, [events, search, imageFilter, originFilter]);

    const unreadable: RootDto[] = status?.roots.filter((r) => !r.readable) ?? [];

    return (
        <div className={styles.root}>
            <div className={styles.headerRow}>
                <div>
                    <Title2>script-warden</Title2>
                    <div>
                        <Caption1>
                            Scripts run through Windows interpreters, captured via IFEO
                            {status ? ` — v${status.version}` : ""}
                        </Caption1>
                    </div>
                </div>
                <Button
                    icon={<ArrowClockwiseRegular />}
                    onClick={() => void load()}
                    appearance="secondary"
                >
                    Refresh
                </Button>
            </div>

            {unreadable.map((r) => (
                <MessageBar key={r.path} intent="warning">
                    <MessageBarBody>
                        <MessageBarTitle>{r.origin} audit root not readable</MessageBarTitle>
                        {r.path}
                        {r.error ? ` — ${r.error}` : ""}. Re-run the viewer elevated to include it.
                    </MessageBarBody>
                </MessageBar>
            ))}

            {error && (
                <MessageBar intent="error">
                    <MessageBarBody>
                        <MessageBarTitle>Failed to load</MessageBarTitle>
                        {error}
                    </MessageBarBody>
                </MessageBar>
            )}

            <div className={styles.toolbar}>
                <div className={styles.search}>
                    <Input
                        placeholder="Filter by command, path, user, parent…"
                        value={search}
                        onChange={(_, d) => setSearch(d.value)}
                        contentBefore={<DocumentTextRegular />}
                    />
                </div>
                <div>
                    <Caption1 block>Image</Caption1>
                    <Dropdown
                        value={imageFilter}
                        selectedOptions={[imageFilter]}
                        onOptionSelect={(_, d) => setImageFilter(d.optionValue ?? ALL)}
                    >
                        {images.map((i) => (
                            <Option key={i} value={i}>
                                {i}
                            </Option>
                        ))}
                    </Dropdown>
                </div>
                <div>
                    <Caption1 block>Origin</Caption1>
                    <Dropdown
                        value={originFilter}
                        selectedOptions={[originFilter]}
                        onOptionSelect={(_, d) => setOriginFilter(d.optionValue ?? ALL)}
                    >
                        {[ALL, "CurrentUser", "System"].map((o) => (
                            <Option key={o} value={o}>
                                {o}
                            </Option>
                        ))}
                    </Dropdown>
                </div>
                <div className={styles.grow} />
                <Caption1>
                    {filtered.length} of {events.length} event(s)
                </Caption1>
            </div>

            {loading ? (
                <Spinner label="Loading audit trail…" />
            ) : (
                <Table aria-label="Audit events" size="small">
                    <TableHeader>
                        <TableRow>
                            <TableHeaderCell>Time (UTC)</TableHeaderCell>
                            <TableHeaderCell>Image</TableHeaderCell>
                            <TableHeaderCell>Origin</TableHeaderCell>
                            <TableHeaderCell>User</TableHeaderCell>
                            <TableHeaderCell>Parent</TableHeaderCell>
                            <TableHeaderCell>Scripts</TableHeaderCell>
                            <TableHeaderCell>Exit</TableHeaderCell>
                        </TableRow>
                    </TableHeader>
                    <TableBody>
                        {filtered.map((e) => (
                            <TableRow
                                key={e.eventId}
                                className={styles.clickable}
                                onClick={() => setSelected(e)}
                            >
                                <TableCell>{fmtTime(e.timestampUtc)}</TableCell>
                                <TableCell>{e.hookedImage}</TableCell>
                                <TableCell>
                                    <Badge
                                        appearance="tint"
                                        color={e.origin === "System" ? "danger" : "informative"}
                                    >
                                        {e.origin}
                                    </Badge>
                                </TableCell>
                                <TableCell>{e.user}</TableCell>
                                <TableCell>{e.parentProcessName}</TableCell>
                                <TableCell>
                                    <TableCellLayout>
                                        {e.scripts.length > 0 ? (
                                            <Badge appearance="filled" color="brand">
                                                {e.scripts.length}
                                            </Badge>
                                        ) : (
                                            <Caption1>—</Caption1>
                                        )}
                                    </TableCellLayout>
                                </TableCell>
                                <TableCell>
                                    {e.exitCode === undefined || e.exitCode === null ? (
                                        "—"
                                    ) : (
                                        <Badge
                                            appearance="tint"
                                            color={e.exitCode === 0 ? "success" : "danger"}
                                        >
                                            {e.exitCode}
                                        </Badge>
                                    )}
                                </TableCell>
                            </TableRow>
                        ))}
                    </TableBody>
                </Table>
            )}

            <EventDialog event={selected} onClose={() => setSelected(null)} />
        </div>
    );
}

function EventDialog({ event, onClose }: { event: AuditEvent | null; onClose: () => void }) {
    const styles = useStyles();
    return (
        <Dialog open={event !== null} onOpenChange={(_, d) => !d.open && onClose()}>
            <DialogSurface>
                {event && (
                    <DialogBody>
                        <DialogTitle>
                            {event.hookedImage} — {fmtTime(event.timestampUtc)}
                        </DialogTitle>
                        <DialogContent>
                            <div className={styles.fieldGrid}>
                                <Text className={styles.label}>Target</Text>
                                <Text>{event.targetPath}</Text>
                                <Text className={styles.label}>Command line</Text>
                                <pre className={styles.mono}>{event.commandLine}</pre>
                                <Text className={styles.label}>User</Text>
                                <Text>
                                    {event.user}
                                    {event.userSid ? ` (${event.userSid})` : ""}
                                </Text>
                                <Text className={styles.label}>Session</Text>
                                <Text>{event.sessionId}</Text>
                                <Text className={styles.label}>Parent</Text>
                                <Text>
                                    {event.parentProcessName} (pid {event.parentProcessId})
                                    {event.parentProcessPath ? ` — ${event.parentProcessPath}` : ""}
                                </Text>
                                <Text className={styles.label}>Working dir</Text>
                                <Text>{event.workingDirectory}</Text>
                                <Text className={styles.label}>Origin</Text>
                                <Text>{event.origin}</Text>
                                <Text className={styles.label}>Exit code</Text>
                                <Text>{event.exitCode ?? "(still running / unknown)"}</Text>
                            </div>

                            <Divider style={{ margin: "16px 0" }} />
                            <Subtitle2>Captured scripts ({event.scripts.length})</Subtitle2>
                            {event.scripts.length === 0 && (
                                <Caption1 block>No script or inline command was captured.</Caption1>
                            )}
                            {event.scripts.map((s, i) => (
                                <ScriptCard key={i} script={s} origin={event.origin} />
                            ))}
                        </DialogContent>
                        <DialogActions>
                            <Button appearance="primary" onClick={onClose}>
                                Close
                            </Button>
                        </DialogActions>
                    </DialogBody>
                )}
            </DialogSurface>
        </Dialog>
    );
}

function ScriptCard({ script, origin }: { script: CapturedScript; origin: string }) {
    const styles = useStyles();
    const [content, setContent] = useState<string | null>(null);
    const [loading, setLoading] = useState(false);
    const [err, setErr] = useState<string | null>(null);
    const hasContent = script.sha256.length > 0;

    async function view() {
        if (content !== null) {
            setContent(null);
            return;
        }
        setLoading(true);
        setErr(null);
        try {
            setContent(await getScript(script, origin));
        } catch (e) {
            setErr(e instanceof Error ? e.message : String(e));
        } finally {
            setLoading(false);
        }
    }

    return (
        <div className={styles.scriptCard}>
            <div className={styles.scriptActions}>
                <Badge appearance="outline">{script.kind}</Badge>
                <Badge appearance="outline" color="brand">
                    {script.language}
                </Badge>
                {script.truncated && (
                    <Badge appearance="tint" color="warning">
                        truncated
                    </Badge>
                )}
                <div className={styles.grow} />
                {hasContent && (
                    <>
                        <Button size="small" onClick={() => void view()}>
                            {content !== null ? "Hide" : "View"}
                        </Button>
                        <Link href={scriptUrl(script, origin, true)} target="_blank">
                            <ArrowDownloadRegular /> Download
                        </Link>
                    </>
                )}
            </div>
            {script.originalPath && (
                <Caption1>
                    Path: {script.originalPath} · {fmtSize(script.sizeBytes)}
                </Caption1>
            )}
            {!script.originalPath && hasContent && <Caption1>{fmtSize(script.sizeBytes)}</Caption1>}
            {script.note && <Caption1>Note: {script.note}</Caption1>}
            {!hasContent && (
                <Caption1>No content stored (see note above).</Caption1>
            )}
            {loading && <Spinner size="tiny" label="Loading…" />}
            {err && <Caption1>Error: {err}</Caption1>}
            {content !== null && <pre className={styles.scriptView}>{content}</pre>}
        </div>
    );
}
