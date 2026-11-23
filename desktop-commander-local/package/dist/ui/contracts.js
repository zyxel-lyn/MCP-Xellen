/**
 * Central constants and shape contracts for UI resource identifiers. It gives one source of truth for URIs/tool metadata shared between server handlers and UI loaders.
 */
export const FILE_PREVIEW_RESOURCE_URI = 'ui://desktop-commander/file-preview';
export const CONFIG_EDITOR_RESOURCE_URI = 'ui://desktop-commander/config-editor';
/**
 * Origins the UI pages may reach. All lists are empty: the pages are self-contained, and an HTML file previewed inside
 * them inherits the policy, so it can't send anything to another origin or load anything from one (fonts from
 * `UI_FONT_ORIGINS` aside).
 */
export const UI_RESOURCE_CSP = {
    connectDomains: [],
    resourceDomains: [],
    frameDomains: [],
    baseUriDomains: [],
};
/**
 * Font origins the pages' own policy allows. Claude's apps send their UI font as `@font-face` rules that load from here.
 * Kept out of `UI_RESOURCE_CSP`: its `resourceDomains` would also allow scripts and images from the origin.
 */
export const UI_FONT_ORIGINS = ['https://assets.claude.ai'];
/**
 * Content-Security-Policy for a `ui.csp` declaration, built the way the MCP Apps spec tells hosts to, without `'self'` (the
 * pages are inlined, and `'self'` would be the host's origin), with local `blob:` images and media, extra font origins,
 * and form posts blocked. An empty list becomes `'none'`.
 */
export function buildUiContentSecurityPolicy(csp, fontOrigins = []) {
    const sources = (fixed, domains = []) => [...fixed, ...domains].join(' ') || "'none'";
    return [
        "default-src 'none'",
        `script-src ${sources(["'unsafe-inline'"], csp.resourceDomains)}`,
        `style-src ${sources(["'unsafe-inline'"], csp.resourceDomains)}`,
        `img-src ${sources(['data:', 'blob:'], csp.resourceDomains)}`,
        `font-src ${sources(fontOrigins, csp.resourceDomains)}`,
        `media-src ${sources(['data:', 'blob:'], csp.resourceDomains)}`,
        `connect-src ${sources([], csp.connectDomains)}`,
        `frame-src ${sources([], csp.frameDomains)}`,
        `base-uri ${sources([], csp.baseUriDomains)}`,
        "form-action 'none'",
    ].join('; ');
}
export function buildUiToolMeta(resourceUri, widgetAccessible = false, showMcpUiPreviews = true) {
    if (!showMcpUiPreviews) {
        return undefined;
    }
    const meta = {
        'ui/resourceUri': resourceUri,
        'openai/outputTemplate': resourceUri,
        ui: {
            resourceUri,
        },
    };
    if (widgetAccessible) {
        meta['openai/widgetAccessible'] = true;
    }
    return meta;
}
