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
    Menu,
    MenuTrigger,
    MenuButton,
    MenuPopover,
    MenuList,
    MenuItemRadio,
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
    WeatherSunnyRegular,
    WeatherMoonRegular,
    DesktopRegular,
    EyeRegular,
    EyeOffRegular,
    ShieldTaskRegular,
    ShieldKeyholeRegular,
    PersonRegular,
    WindowConsoleRegular,
    ChevronRightRegular,
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
    brand: { display: "flex", alignItems: "center", gap: "12px" },
    brandIcon: { fontSize: "34px", color: tokens.colorBrandForeground1, flexShrink: 0 },
    headerActions: { display: "flex", gap: "12px", alignItems: "center" },
    toolbar: { display: "flex", gap: "12px", alignItems: "flex-end", flexWrap: "wrap" },
    pager: { display: "flex", alignItems: "center", gap: "8px" },
    grow: { flex: 1 },
    search: { minWidth: "260px" },
    clickable: { cursor: "pointer" },
    row: { cursor: "pointer", ":hover": { backgroundColor: tokens.colorNeutralBackground1Hover } },
    interpreterIcon: { color: tokens.colorNeutralForeground3 },
    chain: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "2px", rowGap: "6px" },
    chainNode: { display: "inline-flex", alignItems: "center" },
    chainArrow: { color: tokens.colorNeutralForeground4, fontSize: "14px", margin: "0 2px", flexShrink: 0 },
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

export type ThemeMode = "light" | "dark" | "system";

function themeIcon(mode: ThemeMode) {
    if (mode === "light") return <WeatherSunnyRegular />;
    if (mode === "dark") return <WeatherMoonRegular />;
    return <DesktopRegular />;
}

function fmtTime(iso: string): string {
    if (!iso) return "";
    return iso.replace("T", " ").slice(0, 19) + " UTC";
}

function fmtSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function fmtDuration(ms?: number): string {
    if (ms === undefined || ms === null) return "—";
    if (ms < 1000) return `${ms} ms`;
    if (ms < 60000) return `${(ms / 1000).toFixed(1)} s`;
    const m = Math.floor(ms / 60000);
    const s = Math.round((ms % 60000) / 1000);
    return `${m}m ${s}s`;
}

function windowColor(w: string): "success" | "warning" | "subtle" {
    if (w === "Windowed") return "success";
    if (w === "NoWindow") return "warning";
    return "subtle";
}

// The window state matters because a hidden (no-window) launch is how scripts run silently in the
// background, whereas a visible one is an interactive console the user could see.
function windowLabel(w: string): string {
    if (w === "Windowed") return "Visible";
    if (w === "NoWindow") return "Hidden";
    return w || "Unknown";
}

function windowIcon(w: string) {
    return w === "NoWindow" ? <EyeOffRegular /> : <EyeRegular />;
}

function originIcon(o: string) {
    return o === "System" ? <ShieldTaskRegular /> : <PersonRegular />;
}

// Root ancestor → … → immediate parent → the launched process, so the chain reads causally
// left-to-right (each arrow means "started"). The final, emphasized node is this launch itself.
function LaunchChain({ event }: { event: AuditEvent }) {
    const styles = useStyles();
    const parents = [...(event.ancestors ?? [])]
        .reverse()
        .map((a) => ({ name: a.name || "?", pid: a.pid, title: `${a.path || a.name || "?"} (pid ${a.pid})`, self: false }));
    const nodes = [
        ...parents,
        { name: event.hookedImage, pid: event.childProcessId, title: `${event.targetPath} (pid ${event.childProcessId})`, self: true },
    ];
    return (
        <div className={styles.chain}>
            {nodes.map((n, i) => (
                <span key={i} className={styles.chainNode} title={n.title}>
                    <Badge appearance={n.self ? "filled" : "outline"} color={n.self ? "brand" : "informative"}>{n.name}</Badge>
                    {i < nodes.length - 1 && <ChevronRightRegular className={styles.chainArrow} />}
                </span>
            ))}
        </div>
    );
}

export function App({ themeMode, onThemeChange }: { themeMode: ThemeMode; onThemeChange: (m: ThemeMode) => void }) {
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

    // Auto-refresh the current view without resetting filters/pagination. While the server is still
    // indexing a large trail, poll faster so results appear progressively; then settle to 5s.
    const loadRef = useRef(load);
    loadRef.current = load;
    const indexing = status?.indexing ?? false;
    useEffect(() => {
        if (!autoRefresh) return;
        const interval = indexing ? 1000 : 5000;
        const id = setInterval(() => void loadRef.current(true), interval);
        return () => clearInterval(id);
    }, [autoRefresh, indexing]);

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
                <div className={styles.brand}>
                    <ShieldKeyholeRegular className={styles.brandIcon} />
                    <div>
                        <Title2>script-warden</Title2>
                        <div>
                            <Caption1>Scripts that ran on this machine — including ones IT or automation started in the background{status ? ` — v${status.version}` : ""}</Caption1>
                        </div>
                    </div>
                </div>
                <div className={styles.headerActions}>
                    <Switch label="Auto-refresh" checked={autoRefresh} onChange={(_, d) => setAutoRefresh(d.checked)} />
                    <Button icon={<ArrowClockwiseRegular />} onClick={() => void load()} appearance="secondary">Refresh</Button>
                    <Menu
                        checkedValues={{ theme: [themeMode] }}
                        onCheckedValueChange={(_, d) => onThemeChange(d.checkedItems[0] as ThemeMode)}
                    >
                        <MenuTrigger disableButtonEnhancement>
                            <MenuButton appearance="secondary" icon={themeIcon(themeMode)}>Theme</MenuButton>
                        </MenuTrigger>
                        <MenuPopover>
                            <MenuList>
                                <MenuItemRadio name="theme" value="light" icon={<WeatherSunnyRegular />}>Light</MenuItemRadio>
                                <MenuItemRadio name="theme" value="dark" icon={<WeatherMoonRegular />}>Dark</MenuItemRadio>
                                <MenuItemRadio name="theme" value="system" icon={<DesktopRegular />}>System</MenuItemRadio>
                            </MenuList>
                        </MenuPopover>
                    </Menu>
                </div>
            </div>

            <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as string)}>
                <Tab value="audit">Audit</Tab>
                <Tab value="settings">Settings</Tab>
            </TabList>

            {unreadable.map((r) => (
                <MessageBar key={r.path} intent="warning">
                    <MessageBarBody>
                        <MessageBarTitle>Can't read {r.origin} data</MessageBarTitle>
                        Scripts recorded under {r.path}{r.error ? ` (${r.error})` : ""} are hidden. Re-run elevated to include them.
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
                        <FilterDropdown label="Interpreter" value={imageFilter} options={imageOptions} onChange={setFilter(setImageFilter)} />
                        <FilterDropdown label="Origin" value={originFilter} options={[ALL, "CurrentUser", "System"]} onChange={setFilter(setOriginFilter)} />
                        <FilterDropdown label="Parent" value={parentFilter} options={parentOptions} onChange={setFilter(setParentFilter)} />
                        <FilterDropdown label="Visibility" value={windowFilter} options={windowOptions} onChange={setFilter(setWindowFilter)} format={windowLabel} />
                        <div className={styles.grow} />
                        <Button icon={<DeleteRegular />} appearance="secondary" onClick={() => setConfirmClear(true)} disabled={total === 0}>Clear logs</Button>
                    </div>

                    <div className={styles.pager}>
                        <Caption1>
                            {total === 0 ? "No events" : `${pageStart}–${pageEnd} of ${total}`}
                            {lastUpdated ? ` · updated ${lastUpdated.toLocaleTimeString()}` : ""}
                        </Caption1>
                        {indexing && status && (
                            <>
                                <Spinner size="tiny" />
                                <Caption1>indexing {status.eventCount} / {status.totalOnDisk}…</Caption1>
                            </>
                        )}
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
                                    <TableHeaderCell>Interpreter</TableHeaderCell>
                                    <TableHeaderCell>Origin</TableHeaderCell>
                                    <TableHeaderCell>Visibility</TableHeaderCell>
                                    <TableHeaderCell>User</TableHeaderCell>
                                    <TableHeaderCell>Parent</TableHeaderCell>
                                    <TableHeaderCell>Scripts</TableHeaderCell>
                                    <TableHeaderCell>Exit</TableHeaderCell>
                                    <TableHeaderCell>Duration</TableHeaderCell>
                                </TableRow>
                            </TableHeader>
                            <TableBody>
                                {events.map((e) => (
                                    <TableRow key={e.eventId} className={styles.row} onClick={() => setSelected(e)}>
                                        <TableCell>{fmtTime(e.timestampUtc)}</TableCell>
                                        <TableCell><TableCellLayout media={<WindowConsoleRegular className={styles.interpreterIcon} />}>{e.hookedImage}</TableCellLayout></TableCell>
                                        <TableCell><Badge appearance="tint" color={e.origin === "System" ? "danger" : "informative"} icon={originIcon(e.origin)}>{e.origin}</Badge></TableCell>
                                        <TableCell><Badge appearance="tint" color={windowColor(e.window)} icon={windowIcon(e.window)}>{windowLabel(e.window)}</Badge></TableCell>
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
                                        <TableCell>{fmtDuration(e.durationMs)}</TableCell>
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

function FilterDropdown({ label, value, options, onChange, format }: { label: string; value: string; options: string[]; onChange: (v: string) => void; format?: (v: string) => string }) {
    const display = (v: string) => (v === ALL ? "all" : format ? format(v) : v);
    return (
        <div>
            <Caption1 block>{label}</Caption1>
            <Dropdown value={display(value)} selectedOptions={[value]} onOptionSelect={(_, d) => onChange(d.optionValue ?? ALL)}>
                {options.map((o) => (<Option key={o} value={o}>{display(o)}</Option>))}
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
                <Subtitle2>Recording</Subtitle2>
                <Switch label={config.enabled ? "On — scripts are being recorded" : "Off — scripts still run, but nothing is recorded"} checked={config.enabled} onChange={(_, d) => void save({ ...config, enabled: d.checked })} />
            </div>

            <Divider />

            <ExclusionList
                title="Ignored by parent process"
                hint="Skip anything started by these programs — useful to quiet your own tools (e.g. copilot.exe)."
                placeholder="copilot.exe"
                values={config.excludedParents}
                onChange={(v) => void save({ ...config, excludedParents: v })}
            />

            <Divider />

            <ExclusionList
                title="Ignored interpreters"
                hint="Skip these interpreters entirely (e.g. cmd.exe)."
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
                                <Text className={styles.label}>Visibility</Text>
                                <Text>{windowLabel(event.window)}{event.window === "NoWindow" ? " — ran in the background, no console" : ""}</Text>
                                <Text className={styles.label}>User</Text>
                                <Text>{event.user}{event.userSid ? ` (${event.userSid})` : ""}</Text>
                                <Text className={styles.label}>Session</Text>
                                <Text>{event.sessionId}</Text>
                                <Text className={styles.label}>Started by</Text>
                                <Text>{event.parentProcessName} (pid {event.parentProcessId}){event.parentProcessPath ? ` — ${event.parentProcessPath}` : ""}</Text>
                                <Text className={styles.label}>Launch chain</Text>
                                <LaunchChain event={event} />
                                <Text className={styles.label}>Working dir</Text>
                                <Text>{event.workingDirectory}</Text>
                                <Text className={styles.label}>Origin</Text>
                                <Text>{event.origin}</Text>
                                <Text className={styles.label}>Exit code</Text>
                                <Text>{event.exitCode ?? "(still running / unknown)"}</Text>
                                <Text className={styles.label}>Duration</Text>
                                <Text>{event.durationMs === undefined ? "(still running / unknown)" : fmtDuration(event.durationMs)}</Text>
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
