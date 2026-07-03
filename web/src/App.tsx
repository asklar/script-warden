import { useEffect, useRef, useState } from "react";
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
    Switch,
    Link,
    Divider,
    TabList,
    Tab,
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
    DeleteRegular,
    DismissRegular,
    AddRegular,
} from "@fluentui/react-icons";
import {
    AuditEvent,
    CapturedScript,
    RootDto,
    ServeStatus,
    WardenConfig,
    clearLogs,
    getConfig,
    getEvents,
    getScript,
    getStatus,
    saveConfig,
    scriptUrl,
} from "./api";

const useStyles = makeStyles({
    root: { maxWidth: "1200px", margin: "0 auto", padding: "24px", display: "flex", flexDirection: "column", gap: "12px" },
    headerRow: { display: "flex", alignItems: "center", justifyContent: "space-between" },
    headerActions: { display: "flex", gap: "12px", alignItems: "center" },
    toolbar: { display: "flex", gap: "12px", alignItems: "flex-end", flexWrap: "wrap" },
    pager: { display: "flex", alignItems: "center", gap: "8px" },
    grow: { flex: 1 },
    search: { minWidth: "260px" },
    clickable: { cursor: "pointer" },
    mono: { fontFamily: tokens.fontFamilyMonospace, whiteSpace: "pre-wrap", wordBreak: "break-all", background: tokens.colorNeutralBackground3, padding: "8px", borderRadius: tokens.borderRadiusMedium, margin: 0 },
    scriptView: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200, whiteSpace: "pre-wrap", wordBreak: "break-word", background: tokens.colorNeutralBackground3, padding: "12px", borderRadius: tokens.borderRadiusMedium, maxHeight: "320px", overflow: "auto", margin: 0 },
    fieldGrid: { display: "grid", gridTemplateColumns: "140px 1fr", rowGap: "4px", columnGap: "12px", alignItems: "start" },
    label: { color: tokens.colorNeutralForeground3 },
    scriptCard: { border: `1px solid ${tokens.colorNeutralStroke2}`, borderRadius: tokens.borderRadiusMedium, padding: "12px", marginTop: "8px", display: "flex", flexDirection: "column", gap: "6px" },
    scriptActions: { display: "flex", gap: "8px", alignItems: "center" },
    settings: { display: "flex", flexDirection: "column", gap: "20px", maxWidth: "680px" },
    section: { display: "flex", flexDirection: "column", gap: "8px" },
    tagRow: { display: "flex", gap: "8px", flexWrap: "wrap", alignItems: "center" },
    addRow: { display: "flex", gap: "8px", alignItems: "center" },
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

function windowColor(w: string): "success" | "warning" | "subtle" | "informative" {
    if (w === "Visible") return "success";
    if (w === "Hidden") return "warning";
    if (w === "None") return "informative";
    return "subtle";
}

export function App() {
    const styles = useStyles();
    const [tab, setTab] = useState("audit");

    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [events, setEvents] = useState<AuditEvent[]>([]);
    const [total, setTotal] = useState(0);
    const [offset, setOffset] = useState(0);
    const pageSize = 50;
    const [status, setStatus] = useState<ServeStatus | null>(null);
    const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
    const [autoRefresh, setAutoRefresh] = useState(true);
    const inFlight = useRef(false);

    const [search, setSearch] = useState("");
    const [debouncedSearch, setDebouncedSearch] = useState("");
    const [imageFilter, setImageFilter] = useState(ALL);
    const [originFilter, setOriginFilter] = useState(ALL);
    const [parentFilter, setParentFilter] = useState(ALL);
    const [windowFilter, setWindowFilter] = useState(ALL);
    const [selected, setSelected] = useState<AuditEvent | null>(null);
    const [confirmClear, setConfirmClear] = useState(false);

    async function load(silent = false) {
        if (inFlight.current) return;
        inFlight.current = true;
        if (!silent) setLoading(true);
        try {
            const [s, page] = await Promise.all([
                getStatus(),
                getEvents({
                    offset,
                    limit: pageSize,
                    image: imageFilter,
                    origin: originFilter,
                    parent: parentFilter,
                    window: windowFilter,
                    q: debouncedSearch,
                }),
            ]);
            setStatus(s);
            setEvents(page.events);
            setTotal(page.total);
            setLastUpdated(new Date());
            setError(null);
        } catch (err) {
            setError(err instanceof Error ? err.message : String(err));
        } finally {
            inFlight.current = false;
            if (!silent) setLoading(false);
        }
    }

    // Debounce the free-text search; reset to the first page when it changes.
    useEffect(() => {
        const id = setTimeout(() => {
            setDebouncedSearch(search);
            setOffset(0);
        }, 300);
        return () => clearTimeout(id);
    }, [search]);

    // (Re)load whenever the page or any server-side filter changes.
    useEffect(() => {
        void load();
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [offset, imageFilter, originFilter, parentFilter, windowFilter, debouncedSearch]);

    // Auto-refresh the current view without resetting filters/pagination.
    const loadRef = useRef(load);
    loadRef.current = load;
    useEffect(() => {
        if (!autoRefresh) return;
        const id = setInterval(() => void loadRef.current(true), 5000);
        return () => clearInterval(id);
    }, [autoRefresh]);

    function setFilter(setter: (v: string) => void) {
        return (v: string) => {
            setter(v);
            setOffset(0);
        };
    }

    const unreadable: RootDto[] = status?.roots.filter((r) => !r.readable) ?? [];
    const imageOptions = [ALL, ...(status?.images ?? [])];
    const parentOptions = [ALL, ...(status?.parents ?? [])];
    const windowOptions = [ALL, ...(status?.windows ?? [])];

    const pageStart = total === 0 ? 0 : offset + 1;
    const pageEnd = Math.min(offset + pageSize, total);

    async function doClear() {
        setConfirmClear(false);
        try {
            await clearLogs();
            setOffset(0);
            await load();
        } catch (err) {
            setError(err instanceof Error ? err.message : String(err));
        }
    }

    return (
        <div className={styles.root}>
            <div className={styles.headerRow}>
                <div>
                    <Title2>script-warden</Title2>
                    <div>
                        <Caption1>Scripts run through Windows interpreters, captured via IFEO{status ? ` — v${status.version}` : ""}</Caption1>
                    </div>
                </div>
                <div className={styles.headerActions}>
                    <Switch label="Auto-refresh" checked={autoRefresh} onChange={(_, d) => setAutoRefresh(d.checked)} />
                    <Button icon={<ArrowClockwiseRegular />} onClick={() => void load()} appearance="secondary">Refresh</Button>
                </div>
            </div>

            <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as string)}>
                <Tab value="audit">Audit</Tab>
                <Tab value="settings">Settings</Tab>
            </TabList>

            {unreadable.map((r) => (
                <MessageBar key={r.path} intent="warning">
                    <MessageBarBody>
                        <MessageBarTitle>{r.origin} audit root not readable</MessageBarTitle>
                        {r.path}{r.error ? ` — ${r.error}` : ""}. Re-run the viewer elevated to include it.
                    </MessageBarBody>
                </MessageBar>
            ))}

            {error && (
                <MessageBar intent="error">
                    <MessageBarBody><MessageBarTitle>Error</MessageBarTitle>{error}</MessageBarBody>
                </MessageBar>
            )}

            {tab === "audit" ? (
                <>
                    <div className={styles.toolbar}>
                        <div className={styles.search}>
                            <Input placeholder="Filter by command, path, user, parent…" value={search} onChange={(_, d) => setSearch(d.value)} contentBefore={<DocumentTextRegular />} />
                        </div>
                        <FilterDropdown label="Image" value={imageFilter} options={imageOptions} onChange={setFilter(setImageFilter)} />
                        <FilterDropdown label="Origin" value={originFilter} options={[ALL, "CurrentUser", "System"]} onChange={setFilter(setOriginFilter)} />
                        <FilterDropdown label="Parent" value={parentFilter} options={parentOptions} onChange={setFilter(setParentFilter)} />
                        <FilterDropdown label="Window" value={windowFilter} options={windowOptions} onChange={setFilter(setWindowFilter)} />
                        <div className={styles.grow} />
                        <Button icon={<DeleteRegular />} appearance="secondary" onClick={() => setConfirmClear(true)} disabled={total === 0}>Clear logs</Button>
                    </div>

                    <div className={styles.pager}>
                        <Caption1>
                            {total === 0 ? "No events" : `${pageStart}–${pageEnd} of ${total}`}
                            {lastUpdated ? ` · updated ${lastUpdated.toLocaleTimeString()}` : ""}
                        </Caption1>
                        <div className={styles.grow} />
                        <Button size="small" onClick={() => setOffset(Math.max(0, offset - pageSize))} disabled={offset === 0}>Prev</Button>
                        <Button size="small" onClick={() => setOffset(offset + pageSize)} disabled={offset + pageSize >= total}>Next</Button>
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
                                    <TableHeaderCell>Window</TableHeaderCell>
                                    <TableHeaderCell>User</TableHeaderCell>
                                    <TableHeaderCell>Parent</TableHeaderCell>
                                    <TableHeaderCell>Scripts</TableHeaderCell>
                                    <TableHeaderCell>Exit</TableHeaderCell>
                                </TableRow>
                            </TableHeader>
                            <TableBody>
                                {events.map((e) => (
                                    <TableRow key={e.eventId} className={styles.clickable} onClick={() => setSelected(e)}>
                                        <TableCell>{fmtTime(e.timestampUtc)}</TableCell>
                                        <TableCell>{e.hookedImage}</TableCell>
                                        <TableCell><Badge appearance="tint" color={e.origin === "System" ? "danger" : "informative"}>{e.origin}</Badge></TableCell>
                                        <TableCell><Badge appearance="tint" color={windowColor(e.window)}>{e.window}</Badge></TableCell>
                                        <TableCell>{e.user}</TableCell>
                                        <TableCell>{e.parentProcessName}</TableCell>
                                        <TableCell>
                                            <TableCellLayout>
                                                {e.scripts.length > 0 ? <Badge appearance="filled" color="brand">{e.scripts.length}</Badge> : <Caption1>—</Caption1>}
                                            </TableCellLayout>
                                        </TableCell>
                                        <TableCell>
                                            {e.exitCode === undefined || e.exitCode === null ? "—" : <Badge appearance="tint" color={e.exitCode === 0 ? "success" : "danger"}>{e.exitCode}</Badge>}
                                        </TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    )}
                </>
            ) : (
                <SettingsView onError={setError} />
            )}

            <EventDialog event={selected} onClose={() => setSelected(null)} />

            <Dialog open={confirmClear} onOpenChange={(_, d) => !d.open && setConfirmClear(false)}>
                <DialogSurface>
                    <DialogBody>
                        <DialogTitle>Clear all audit data?</DialogTitle>
                        <DialogContent>This permanently deletes all captured events and scripts from the readable roots. This cannot be undone.</DialogContent>
                        <DialogActions>
                            <Button appearance="secondary" onClick={() => setConfirmClear(false)}>Cancel</Button>
                            <Button appearance="primary" icon={<DeleteRegular />} onClick={() => void doClear()}>Clear everything</Button>
                        </DialogActions>
                    </DialogBody>
                </DialogSurface>
            </Dialog>
        </div>
    );
}

function FilterDropdown({ label, value, options, onChange }: { label: string; value: string; options: string[]; onChange: (v: string) => void }) {
    return (
        <div>
            <Caption1 block>{label}</Caption1>
            <Dropdown value={value} selectedOptions={[value]} onOptionSelect={(_, d) => onChange(d.optionValue ?? ALL)}>
                {options.map((o) => (<Option key={o} value={o}>{o}</Option>))}
            </Dropdown>
        </div>
    );
}

function SettingsView({ onError }: { onError: (e: string) => void }) {
    const styles = useStyles();
    const [config, setConfig] = useState<WardenConfig | null>(null);
    const [saving, setSaving] = useState(false);
    const [savedAt, setSavedAt] = useState<Date | null>(null);

    useEffect(() => {
        getConfig().then(setConfig).catch((e) => onError(e instanceof Error ? e.message : String(e)));
    }, []);

    if (!config) return <Spinner label="Loading settings…" />;

    async function save(next: WardenConfig) {
        setConfig(next);
        setSaving(true);
        try {
            const saved = await saveConfig(next);
            setConfig(saved);
            setSavedAt(new Date());
        } catch (e) {
            onError(e instanceof Error ? e.message : String(e));
        } finally {
            setSaving(false);
        }
    }

    return (
        <div className={styles.settings}>
            <div className={styles.section}>
                <Subtitle2>Auditing</Subtitle2>
                <Switch label={config.enabled ? "Enabled" : "Disabled (launches still run, nothing is recorded)"} checked={config.enabled} onChange={(_, d) => void save({ ...config, enabled: d.checked })} />
            </div>

            <Divider />

            <ExclusionList
                title="Excluded parent processes"
                hint="Launches whose parent process matches are not audited (e.g. copilot.exe)."
                placeholder="copilot.exe"
                values={config.excludedParents}
                onChange={(v) => void save({ ...config, excludedParents: v })}
            />

            <Divider />

            <ExclusionList
                title="Excluded images"
                hint="Hooked interpreters to skip entirely (e.g. cmd.exe)."
                placeholder="cmd.exe"
                values={config.excludedImages}
                onChange={(v) => void save({ ...config, excludedImages: v })}
            />

            <div>
                {saving ? <Caption1>Saving…</Caption1> : savedAt && <Caption1>Saved {savedAt.toLocaleTimeString()}</Caption1>}
            </div>
        </div>
    );
}

function ExclusionList({ title, hint, placeholder, values, onChange }: { title: string; hint: string; placeholder: string; values: string[]; onChange: (v: string[]) => void }) {
    const styles = useStyles();
    const [draft, setDraft] = useState("");

    function add() {
        const v = draft.trim();
        if (!v || values.some((x) => x.toLowerCase() === v.toLowerCase())) {
            setDraft("");
            return;
        }
        onChange([...values, v]);
        setDraft("");
    }

    return (
        <div className={styles.section}>
            <Subtitle2>{title}</Subtitle2>
            <Caption1>{hint}</Caption1>
            <div className={styles.tagRow}>
                {values.length === 0 && <Caption1>(none)</Caption1>}
                {values.map((v) => (
                    <Badge key={v} appearance="tint" color="informative">
                        {v}
                        <Button size="small" appearance="transparent" icon={<DismissRegular />} aria-label={`Remove ${v}`} onClick={() => onChange(values.filter((x) => x !== v))} />
                    </Badge>
                ))}
            </div>
            <div className={styles.addRow}>
                <Input value={draft} placeholder={placeholder} onChange={(_, d) => setDraft(d.value)} onKeyDown={(e) => { if (e.key === "Enter") add(); }} />
                <Button icon={<AddRegular />} onClick={add}>Add</Button>
            </div>
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
                        <DialogTitle>{event.hookedImage} — {fmtTime(event.timestampUtc)}</DialogTitle>
                        <DialogContent>
                            <div className={styles.fieldGrid}>
                                <Text className={styles.label}>Target</Text>
                                <Text>{event.targetPath}</Text>
                                <Text className={styles.label}>Command line</Text>
                                <pre className={styles.mono}>{event.commandLine}</pre>
                                <Text className={styles.label}>Window</Text>
                                <Text>{event.window}</Text>
                                <Text className={styles.label}>User</Text>
                                <Text>{event.user}{event.userSid ? ` (${event.userSid})` : ""}</Text>
                                <Text className={styles.label}>Session</Text>
                                <Text>{event.sessionId}</Text>
                                <Text className={styles.label}>Parent</Text>
                                <Text>{event.parentProcessName} (pid {event.parentProcessId}){event.parentProcessPath ? ` — ${event.parentProcessPath}` : ""}</Text>
                                <Text className={styles.label}>Working dir</Text>
                                <Text>{event.workingDirectory}</Text>
                                <Text className={styles.label}>Origin</Text>
                                <Text>{event.origin}</Text>
                                <Text className={styles.label}>Exit code</Text>
                                <Text>{event.exitCode ?? "(still running / unknown)"}</Text>
                            </div>

                            <Divider style={{ margin: "16px 0" }} />
                            <Subtitle2>Captured scripts ({event.scripts.length})</Subtitle2>
                            {event.scripts.length === 0 && <Caption1 block>No script or inline command was captured.</Caption1>}
                            {event.scripts.map((s, i) => (<ScriptCard key={i} script={s} origin={event.origin} />))}
                        </DialogContent>
                        <DialogActions>
                            <Button appearance="primary" onClick={onClose}>Close</Button>
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
                <Badge appearance="outline" color="brand">{script.language}</Badge>
                {script.truncated && <Badge appearance="tint" color="warning">truncated</Badge>}
                <div className={styles.grow} />
                {hasContent && (
                    <>
                        <Button size="small" onClick={() => void view()}>{content !== null ? "Hide" : "View"}</Button>
                        <Link href={scriptUrl(script, origin, true)} target="_blank"><ArrowDownloadRegular /> Download</Link>
                    </>
                )}
            </div>
            {script.originalPath && <Caption1>Path: {script.originalPath} · {fmtSize(script.sizeBytes)}</Caption1>}
            {!script.originalPath && hasContent && <Caption1>{fmtSize(script.sizeBytes)}</Caption1>}
            {script.note && <Caption1>Note: {script.note}</Caption1>}
            {!hasContent && <Caption1>No content stored (see note above).</Caption1>}
            {loading && <Spinner size="tiny" label="Loading…" />}
            {err && <Caption1>Error: {err}</Caption1>}
            {content !== null && <pre className={styles.scriptView}>{content}</pre>}
        </div>
    );
}
