/**
 * Central constants and shape contracts for UI resource identifiers. It gives one source of truth for URIs/tool metadata shared between server handlers and UI loaders.
 */
export declare const FILE_PREVIEW_RESOURCE_URI = "ui://desktop-commander/file-preview";
export declare const CONFIG_EDITOR_RESOURCE_URI = "ui://desktop-commander/config-editor";
/**
 * A UI resource's `_meta.ui.csp`: the MCP Apps spec's `McpUiResourceCsp`. ext-apps defines it, but re-exports it through
 * paths that Node16 module resolution can't follow, so the shape is repeated here.
 */
export interface McpUiResourceCsp {
    connectDomains?: string[];
    resourceDomains?: string[];
    frameDomains?: string[];
    baseUriDomains?: string[];
}
/**
 * Origins the UI pages may reach. All lists are empty: the pages are self-contained, and an HTML file previewed inside
 * them inherits the policy, so it can't send anything to another origin or load anything from one (fonts from
 * `UI_FONT_ORIGINS` aside).
 */
export declare const UI_RESOURCE_CSP: McpUiResourceCsp;
/**
 * Font origins the pages' own policy allows. Claude's apps send their UI font as `@font-face` rules that load from here.
 * Kept out of `UI_RESOURCE_CSP`: its `resourceDomains` would also allow scripts and images from the origin.
 */
export declare const UI_FONT_ORIGINS: string[];
/**
 * Content-Security-Policy for a `ui.csp` declaration, built the way the MCP Apps spec tells hosts to, without `'self'` (the
 * pages are inlined, and `'self'` would be the host's origin), with local `blob:` images and media, extra font origins,
 * and form posts blocked. An empty list becomes `'none'`.
 */
export declare function buildUiContentSecurityPolicy(csp: McpUiResourceCsp, fontOrigins?: string[]): string;
export interface UiToolMeta extends Record<string, unknown> {
    'ui/resourceUri': string;
    'openai/outputTemplate': string;
    ui: {
        resourceUri: string;
    };
    'openai/widgetAccessible'?: boolean;
}
export declare function buildUiToolMeta(resourceUri: string, widgetAccessible?: boolean, showMcpUiPreviews?: boolean): UiToolMeta | undefined;
